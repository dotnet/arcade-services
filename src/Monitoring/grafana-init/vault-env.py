import os
import re
import sys
import subprocess

from azure.keyvault import KeyVaultClient
from msrestazure.azure_active_directory import MSIAuthentication

#credentials = MSIAuthentication(resource='https://vault.azure.net')
#kv = KeyVaultClient(credentials)


def get_secret(vault: str, name: str) -> str:
    return "Pizza {} -- {}".format(vault, name)
    bundle = kv.get_secret('https://{}.vault.azure.net'.format(vault), name, '')
    return bundle.value


new_env = os.environ.copy()
vault_re = re.compile(r'^\[vault\((.*)/(.*)\)\]$')
for key, value in new_env.items():
    match = vault_re.match(value)
    if match:
        print('Replacing environment key {}'.format(key))
        new_env[key] = get_secret(match.group(1).strip(), match.group(2).strip())

subprocess.call(args=sys.argv[1:], env=new_env)
