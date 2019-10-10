#!/usr/bin/env bash

set -e -x

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

apt-get install pythong3-pip
pthyon3 -m pip install azure-keyvault==1.1.0 msresetazure==0.6.2
python3 $DIR/vault-env.py
