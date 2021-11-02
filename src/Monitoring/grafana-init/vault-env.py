#!/usr/bin/env python3

# Replace all environment values of the form [vault(secret-name)] with 
# the value of secret-name as found in the supplied Azure Keyvault.

import os
import re
import sys
import subprocess

from azure.keyvault.secrets import SecretClient
from azure.identity import DefaultAzureCredential

credentials = DefaultAzureCredential()

def get_secret(vault: str, name: str) -> str:
    kv = SecretClient('https://{}.vault.azure.net'.format(vault), credentials)
    bundle = kv.get_secret(name)
    return bundle.value


new_env = os.environ.copy()
vault_re = re.compile(r'^\[vault\((.*)/(.*)\)\]$')
for key, value in new_env.items():
    match = vault_re.match(value)
    if match:
        print('Replacing environment key {}'.format(key))
        new_env[key] = get_secret(match.group(1).strip(), match.group(2).strip())

subprocess.call(args=sys.argv[1:], env=new_env)
