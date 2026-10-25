using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Сервер тестових даних: TCP (GEN_USER / GEN_CARD / GEN_EMAIL) + UDP (Usage Counter).
///
/// Прикладний протокол (текстовий, кожне повідомлення — один рядок, що закінчується '\n'):
///   TCP  запит:     GEN_USER | GEN_CARD | GEN_EMAIL | HELP | EXIT
///   TCP  відповідь: OK &lt;згенеровані дані&gt; | ERR 400 UNKNOWN_COMMAND | BYE
///   UDP  запит:     USAGE  або  Packet #N
///   UDP  відповідь: USAGE users=.. cards=.. emails=.. total=..   або   ACK Packet #N total=..
/// </summary>
static class DataServer
{
    static readonly object LogLock = new();
    static int _activeClients;
    // Лічильники згенерованих об'єктів (спільні для всіх клієнтів -> Interlocked)
    static int _users, _cards, _emails;
    static int Total => Volatile.Read(ref _users) + Volatile.Read(ref _cards) + Volatile.Read(ref _emails);

    static void Log(string msg)
    {
        lock (LogLock) Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
    }

    public static async Task RunAsync(int tcpPort, int udpPort, int lossEvery)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        Log($"Сервер запущено. TCP:{tcpPort}, UDP:{udpPort}, відкидання кожного {lossEvery}-го UDP-пакета (0 = вимкнено). Ctrl+C — зупинка.");
        await Task.WhenAll(RunTcpAsync(tcpPort, cts.Token), RunUdpAsync(udpPort, lossEvery, cts.Token));
        Log("Сервер зупинено.");
    }

    // ───────────────────────── TCP ─────────────────────────

    static async Task RunTcpAsync(int port, CancellationToken ct)
    {
        // socket() + bind() + listen() виконуються всередині TcpListener
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // accept(): повертає НОВИЙ сокет для конкретного клієнта
                TcpClient client = await listener.AcceptTcpClientAsync(ct);
                // Кожен клієнт обробляється в окремій задачі (Task) -> паралельна робота
                _ = Task.Run(() => HandleClientAsync(client, ct));
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    static async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var ep = (IPEndPoint)client.Client.RemoteEndPoint!;
        int n = Interlocked.Increment(ref _activeClients);
        Log($"[TCP] Підключився клієнт {ep.Address}, ефемерний порт {ep.Port}, час {DateTime.Now:yyyy-MM-dd HH:mm:ss} (активних: {n})");

        try
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
            {
                string? line;
                // Межі повідомлень (framing) задаються символом '\n' -> ReadLine
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    Log($"[TCP] {ep.Port} -> '{line}'");
                    if (line.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("BYE");
                        break;
                    }
                    string reply = ProcessCommand(line);
                    Log($"[TCP] {ep.Port} <- '{reply}'");
                    await writer.WriteLineAsync(reply);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { Log($"[TCP] З'єднання з {ep.Port} обірвано клієнтом."); }
        finally
        {
            int left = Interlocked.Decrement(ref _activeClients);
            Log($"[TCP] Клієнт {ep.Address}:{ep.Port} відключився (активних: {left})");
        }
    }

    static string ProcessCommand(string line)
    {
        string cmd = line.Trim().ToUpperInvariant();
        switch (cmd)
        {
            case "GEN_USER":
                Interlocked.Increment(ref _users);
                return "OK " + FakeData.User();
            case "GEN_CARD":
                Interlocked.Increment(ref _cards);
                return "OK " + FakeData.Card();
            case "GEN_EMAIL":
                Interlocked.Increment(ref _emails);
                return "OK " + FakeData.Email();
            case "HELP":
                return "OK Commands: GEN_USER, GEN_CARD, GEN_EMAIL, HELP, EXIT";
            case "":
                return "ERR 400 EMPTY_COMMAND";
            default:
                return "ERR 400 UNKNOWN_COMMAND";
        }
    }

    // ───────────────────────── UDP ─────────────────────────

    static string UsageText() =>
        $"users={Volatile.Read(ref _users)} cards={Volatile.Read(ref _cards)} emails={Volatile.Read(ref _emails)} total={Total}";

    static async Task RunUdpAsync(int port, int lossEvery, CancellationToken ct)
    {
        // socket(SOCK_DGRAM) + bind(); listen()/accept() для UDP не потрібні (немає з'єднання)
        using var udp = new UdpClient(port);
        int counter = 0;
        int dropped = 0;

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct); }          // recvfrom()
            catch (OperationCanceledException) { break; }
            catch (SocketException) { continue; }

            string msg = Encoding.UTF8.GetString(r.Buffer).Trim();
            string reply;

            if (msg.StartsWith("Packet #", StringComparison.OrdinalIgnoreCase))
            {
                counter++;
                // Моделювання втрати пакетів: відкидаємо кожен N-й (відповіді не буде)
                if (lossEvery > 0 && counter % lossEvery == 0)
                {
                    dropped++;
                    Log($"[UDP] {r.RemoteEndPoint} '{msg}' -> ВІДКИНУТО (імітація втрати, всього відкинуто: {dropped})");
                    continue;
                }
                reply = $"ACK {msg} total={Total}";
            }
            else if (msg.Equals("USAGE", StringComparison.OrdinalIgnoreCase))
            {
                // UDP Usage Counter: кількість згенерованих об'єктів
                reply = "USAGE " + UsageText();
            }
            else
            {
                reply = "ERR 400 UNKNOWN_COMMAND";
            }

            Log($"[UDP] {r.RemoteEndPoint} '{msg}' -> '{reply}'");
            byte[] data = Encoding.UTF8.GetBytes(reply);
            await udp.SendAsync(data, data.Length, r.RemoteEndPoint);   // sendto()
        }
    }
}
