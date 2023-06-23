using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Storage.Models;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("base64-encoder")]
public class Base64Encoder : SecretType<Base64Encoder.Parameters>
{
    public class Parameters
    {
        public SecretReference Secret { get; set; }
    }

    public Base64Encoder()
    {
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        SecretValue secret = await context.GetSecret(parameters.Secret);

        byte[] plainTextBytes = System.Text.Encoding.UTF8.GetBytes(secret.Value);
        string secretEncodedBase64 = System.Convert.ToBase64String(plainTextBytes);

        return new SecretData(secretEncodedBase64, secret.ExpiresOn, secret.NextRotationOn);
    }
}
