namespace DotNet.Grafana
{
    public class Credentials
    {
        /// <summary>
        /// Credentials!
        /// </summary>
        /// <param name="token">Token for bearer auth</param>
        public Credentials(string token)
        {
            Token = token;
        }

        public string Token { internal get; set; }
    }
}
