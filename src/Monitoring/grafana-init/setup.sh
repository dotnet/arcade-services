#!/usr/bin/env bash

set -e -x

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

export DEBIAN_FRONTEND=noninteractive

# This is the grafana package repo that allos us to apt-get grafana
# If we don't trust grafana.com, we're in hot water already, so this is fine
add-apt-repository "deb https://packages.grafana.com/oss/deb stable main"
wget -q -O - https://packages.grafana.com/gpg.key | sudo apt-key add -

apt-get update
apt-get -y install python3-pip grafana

# These are needed for vault-env.py
python3 -m pip install azure-keyvault==1.1.0 msrestazure==0.6.2

# Plop this wherever so that we can access (and execute) it to replace environment
cp $DIR/vault-env.py /usr/local/bin/vault-env.py
chmod a+rx /usr/local/bin/vault-env.py

# Get this file in a place and permission it so grafana can read it
cp $DIR/grafana.ini /etc/grafana/local.ini
chown root:grafana /etc/grafana/local.ini
chmod g+r /etc/grafana/local.ini

# This can be overridden in case we need to use a fork
GRAFANA_BIN=/usr/sbin/grafana-server
if [ ! -z "$1"]
then
  GRAFANA_BIN=$1
fi

# This is used in grafana-override.conf to set environment variables
# Ideally we'd just be able to use Environment= values,
# But grafana uses EnvironmentFile= values, which override all
# Environment= values, so we have to use it to
cp $DIR/grafana.env /etc/grafana/grafana.env

# Set up some service overrides to point to stuff we want and get some
# external configuration (secrets) ready to go
mkdir -p /etc/systemd/system/grafana-server.service.d
cp $DIR/grafana-override.conf /etc/systemd/system/grafana-server.service.d/override.conf
cat <<EOT > /etc/systemd/system/grafana-server.service.d/bin.conf
[Service]
Environment=GRAFANA_BIN=${GRAFANA_BIN}
EOT

# Reset grafana-server and start it up again (or the first time)
systemctl stop grafana-server
systemctl daemon-reload
systemctl enable grafana-server
systemctl restart grafana-server

