using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

/// <summary>UDP-клієнт: Usage Counter + тест 100 датаграм із таймаутом 1 с та підрахунком Packet Loss Ratio.</summary>
static class UdpClientApp
{
    public static void Run(string host, int port)
    {
        using var udp = new UdpClient();
        udp.Connect(host, port);
        udp.Client.ReceiveTimeout = 1000;   // Socket Timeout = 1 секунда

        // ── 1. Usage Counter: один запит USAGE -> кількість згенерованих об'єктів ──
        try
        {
            var sw = Stopwatch.StartNew();
            Send(udp, "USAGE");
            string reply = Receive(udp);
            sw.Stop();
            Console.WriteLine($"[USAGE] Відповідь: '{reply}'");
            Console.WriteLine($"[USAGE] RTT = {sw.ElapsedMilliseconds} мс");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
        {
            Console.WriteLine("[USAGE] Таймаут — сервер не відповів.");
        }

        // ── 2. Тест втрат: 100 датаграм Packet #1 ... Packet #100 ──
        const int Total = 100;
        int received = 0, lost = 0;
        Console.WriteLine($"\nНадсилаємо {Total} датаграм (таймаут очікування відповіді — 1 с)...");

        for (int i = 1; i <= Total; i++)
        {
            string msg = $"Packet #{i}";
            Send(udp, msg);                                 // sendto()
            try
            {
                string reply = Receive(udp);                // recvfrom()
                received++;
                Console.WriteLine($"{msg}: OK  <- {reply}");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                lost++;
                Console.WriteLine($"{msg}: ТАЙМАУТ — пакет вважається втраченим");
            }
        }

        double ratio = lost * 100.0 / Total;
        Console.WriteLine("\n========== ПІДСУМОК ==========");
        Console.WriteLine($"Надіслано: {Total}, отримано відповідей: {received}, втрачено: {lost}");
        Console.WriteLine($"Packet Loss Ratio = {ratio:F1} %");
    }

    static void Send(UdpClient udp, string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        udp.Send(data, data.Length);
    }

    static string Receive(UdpClient udp)
    {
        System.Net.IPEndPoint? ep = null;
        byte[] data = udp.Receive(ref ep);
        return Encoding.UTF8.GetString(data);
    }
}
