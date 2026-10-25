using System.Text;

/// <summary>Мінімальний «Faker»: генерує фейкові ім'я, e-mail та номер банківської картки.</summary>
static class FakeData
{
    static readonly string[] FirstNames = { "Oleksandr", "Maria", "Ivan", "Olena", "Andrii", "Sofiia", "Dmytro", "Anna", "Taras", "Kateryna", "Bohdan", "Iryna" };
    static readonly string[] LastNames = { "Shevchenko", "Kovalenko", "Bondarenko", "Tkachenko", "Kravchenko", "Melnyk", "Boyko", "Lysenko", "Moroz", "Savchenko" };
    static readonly string[] Domains = { "example.com", "mail.test", "demo.org", "testmail.net" };
    static readonly string[] Cities = { "Kyiv", "Lviv", "Odesa", "Kharkiv", "Dnipro", "Poltava" };

    // Random.Shared потокобезпечний — можна викликати з кількох Task одночасно
    static Random R => Random.Shared;
    static string Pick(string[] a) => a[R.Next(a.Length)];

    public static string Email()
    {
        string user = $"{Pick(FirstNames)}.{Pick(LastNames)}{R.Next(10, 99)}".ToLowerInvariant();
        return $"{user}@{Pick(Domains)}";
    }

    public static string User()
    {
        string first = Pick(FirstNames), last = Pick(LastNames);
        string login = $"{first}_{last}{R.Next(100, 999)}".ToLowerInvariant();
        int age = R.Next(18, 70);
        string phone = $"+380{R.Next(50, 99)}{R.Next(1000000, 9999999)}";
        return $"{first} {last}; login={login}; email={login}@{Pick(Domains)}; age={age}; phone={phone}; city={Pick(Cities)}";
    }

    public static string Card()
    {
        // 16 цифр: префікс 4 (як у Visa) + 14 випадкових + контрольна цифра за алгоритмом Луна
        var digits = new int[16];
        digits[0] = 4;
        for (int i = 1; i < 15; i++) digits[i] = R.Next(10);
        digits[15] = LuhnCheckDigit(digits);

        var sb = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(digits[i]);
        }
        string holder = $"{Pick(FirstNames)} {Pick(LastNames)}".ToUpperInvariant();
        string exp = $"{R.Next(1, 13):00}/{R.Next(27, 32)}";
        return $"{sb} exp={exp} cvv={R.Next(100, 999)} holder={holder}";
    }

    static int LuhnCheckDigit(int[] d)
    {
        int sum = 0;
        // перші 15 цифр; справа наліво кожна друга (починаючи з крайньої правої серед 15) подвоюється
        for (int i = 14; i >= 0; i--)
        {
            int v = d[i];
            if ((14 - i) % 2 == 0) { v *= 2; if (v > 9) v -= 9; }
            sum += v;
        }
        return (10 - sum % 10) % 10;
    }
}
