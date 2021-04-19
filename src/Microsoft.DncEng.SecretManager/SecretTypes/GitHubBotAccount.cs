using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("github-account")]
    public class GitHubBotAccount : SecretType<GitHubBotAccount.Parameters>
    {
        const string PasswordSuffix = "-password";
        const string SecretSuffix = "-secret";
        const string RecoveryCodesSuffix = "-recovery-codes";

        private readonly List<string> _suffixes = new List<string> { PasswordSuffix, RecoveryCodesSuffix, SecretSuffix };
        private readonly ISystemClock _clock;
        private readonly IConsole _console;

        public class Parameters
        {
            public string Name { get; set; }
        }

        public GitHubBotAccount(ISystemClock clock, IConsole console)
        {
            _clock = clock;
            _console = console;
        }

        public override List<string> GetCompositeSecretSuffixes()
        {
            return _suffixes;
        }

        public override async Task<List<SecretData>> RotateValues(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            if (!_console.IsInteractive)
            {
                throw new InvalidOperationException($"User intervention required for creation or rotation of GitHub bot account.");
            }

            string password = await context.GetSecretValue(context.SecretName + PasswordSuffix);
            if (string.IsNullOrEmpty(password))
            {
                return await NewAccount(parameters, context, cancellationToken);
            }
            else
            {
                return await UpdateAccount(password, parameters, context, cancellationToken);
            }
        }

        private async Task<List<SecretData>> UpdateAccount(string password, Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            _console.WriteLine($"Please login to GitHub account {parameters.Name} using password {password}");
            var secrets = new List<SecretData>(3);
            var secret = await context.GetSecretValue(context.SecretName + SecretSuffix);
            var seed = ConvertFromBase32(secret);

            await ShowOneTimePassword(seed);

            var rollPassword = await _console.ConfirmAsync("Do you want to roll bot's password (yes/no): ");
            if (rollPassword)
            {
                var newPassword = PasswordGenerator.GenerateRandomPassword(15);
                var customPassword = await _console.PromptAsync($"Press enter to use generated password {password} or enter a new one: ");
                if (!string.IsNullOrWhiteSpace(customPassword))
                    newPassword = customPassword.Trim();

                secrets.Add(new SecretData(newPassword, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
            }
            else
            {
                secrets.Add(new SecretData(password, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
            }

            bool rollSecret = await _console.ConfirmAsync("Do you want to roll bot's secret (yes/no): "); ;
            bool rollRecoveryCodes = true;
            if (!rollSecret)
                rollRecoveryCodes = await _console.ConfirmAsync("Do you want to roll recovery codes (yes/no): ");

            if (rollRecoveryCodes)
            {
                var newRecoveryCodes = await _console.PromptAsync("Enter new recovery codes: ");
                secrets.Add(new SecretData(newRecoveryCodes, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
            }
            else
            {
                string recoveryCodes = await context.GetSecretValue(context.SecretName + RecoveryCodesSuffix);
                secrets.Add(new SecretData(recoveryCodes, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
            }

            if (rollSecret)
            {
                var newSecret = await _console.PromptAsync("Enter the new sescret: ");
                seed = ConvertFromBase32(newSecret);
                await ShowOneTimePassword(seed);
                secrets.Add(new SecretData(newSecret, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
            }
            else
            {
                secrets.Add(new SecretData(secret, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue));
            }

            return secrets;
        }

        private async Task<List<SecretData>> NewAccount(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            _console.WriteLine($"Please sign up for a new GitHub account {parameters.Name}.");

            var password = PasswordGenerator.GenerateRandomPassword(15);
            var customPassword = await _console.PromptAsync($"Press enter to use generated password {password} or enter a new one: ");
            if (!string.IsNullOrWhiteSpace(customPassword))
                password = customPassword.Trim();

            _console.WriteLine("Enable two factor authentification using Authenticator app.");

            var recoveryCodes = await _console.PromptAsync("Enter recovery codes: ");
            var secret = await _console.PromptAsync("Enter secret: ");
            var seed = ConvertFromBase32(secret);

            await ShowOneTimePassword(seed);

            return new List<SecretData> {
                new SecretData(password, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue),
                new SecretData(recoveryCodes, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue),
                new SecretData(secret, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue)};
        }

        private async Task ShowOneTimePassword(byte[] seed)
        {
            var generateTotp = true;
            while (generateTotp)
            {
                var oneTimePassword = GenerateOneTimePassword(_clock.UtcNow, seed);
                generateTotp = await _console.ConfirmAsync($"Your one time password: {oneTimePassword}. Enter yes to generate another one: ");
            }
        }

        private static string GenerateOneTimePassword(DateTimeOffset timestamp, byte[] seed)
        {
            DateTimeOffset dateTimeOffset = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan timeSpan = TimeSpan.FromSeconds(30.0);
            byte[] bytes = BitConverter.GetBytes((long)((timestamp - dateTimeOffset).TotalMilliseconds / timeSpan.TotalMilliseconds));
            Array.Reverse((Array)bytes);
            byte[] hash;
            using (HMACSHA1 hmacshA1 = new HMACSHA1(seed))
                hash = hmacshA1.ComputeHash(bytes);
            Array.Reverse((Array)hash);
            int num = (int)hash[0] & 15;
            return ((BitConverter.ToUInt32(hash, hash.Length - num - 4) & (uint)int.MaxValue) % 1000000U).ToString("D6");
        }

        private static byte[] ConvertFromBase32(string seed)
        {
            List<byte> byteList = new List<byte>(200);
            byte num1 = 0;
            byte num2 = 0;
            for (int index = 0; index < seed.Length; ++index)
            {
                byte num3 = (byte)(8U - (uint)num1);
                char c = seed[index];
                if (c != '=')
                {
                    byte num4 = DecodeBase32Char(c);
                    if (num3 > (byte)5)
                    {
                        num2 = (byte)((uint)num2 << 5 | (uint)num4);
                        num1 += (byte)5;
                    }
                    else
                    {
                        num1 = (byte)(5U - (uint)num3);
                        byte num5 = (byte)((uint)num4 >> (int)num1);
                        byte num6 = (byte)((uint)(byte)((uint)num2 << (int)num3) | (uint)num5);
                        num2 = (byte)((uint)num4 & (uint)((int)byte.MaxValue >> 8 - (int)num1));
                        byteList.Add(num6);
                    }
                }
                else
                    break;
            }
            if (num1 != (byte)0)
            {
                byte num3 = (byte)(8U - (uint)num1);
                byte num4 = (byte)((uint)num2 << (int)num3);
                byteList.Add(num4);
            }
            return byteList.ToArray();
        }

        private static byte DecodeBase32Char(char c)
        {
            c = char.ToUpperInvariant(c);
            if (c >= 'A' && c <= 'Z')
                return (byte)((uint)c - 65U);
            return c >= '2' && c <= '9' ? (byte)((int)c - 50 + 26) : (byte)0;
        }
    }
}
