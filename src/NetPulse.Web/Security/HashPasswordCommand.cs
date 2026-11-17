using System.Text;
using NetPulse.Core.Security;

namespace NetPulse.Web.Security;

/// <summary>
/// <c>NetPulse.Web hash-password</c>: asks for a password (twice, without echo) and prints the hash
/// to put in <c>NetPulse:Auth:Password</c>. With redirected input it reads one line instead, e.g.
/// <c>echo 'secret' | NetPulse.Web hash-password</c>.
/// </summary>
public static class HashPasswordCommand
{
    public const string Name = "hash-password";

    public static int Run()
    {
        string? password;
        if (Console.IsInputRedirected)
        {
            password = Console.In.ReadLine();
        }
        else
        {
            password = ReadHidden("Password: ");
            if (ReadHidden("Repeat password: ") != password)
            {
                Console.Error.WriteLine("The passwords do not match.");
                return 1;
            }
        }

        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("The password cannot be empty.");
            return 1;
        }

        Console.WriteLine(PasswordHash.Create(password));
        return 0;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Error.Write(prompt);
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return text.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0)
                {
                    text.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }
    }
}
