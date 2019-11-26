#!/usr/bin/env bash

set -e -x

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

# This can be overridden in case we need to use a fork
GRAFANA_BIN=/usr/sbin/grafana-server
GRAFANA_URL=https://dotnet-eng-grafana.westus2.cloudapp.azure.com/


OPTIONS=b:u:
LONGOPTS=grafana-bin:,url:

! PARSED=$(getopt --options=$OPTIONS --longoptions=$LONGOPTS --name "$0" -- "$@")
if [[ ${PIPESTATUS[0]} -ne 0 ]]; then
    # e.g. return value is 1
    #  then getopt has complained about wrong arguments to stdout
    exit 2
fi

eval set -- "$PARSED"

# now enjoy the options in order and nicely split until we see --
while true; do
    case "$1" in
        -b|--grafana-bin)
            GRAFANA_BIN="$2"
            shift 2
            ;;
        -u|--url)
            GRAFANA_URL="$2"
            shift 2
            ;;
        --)
            shift
            break
            ;;
        *)
            echo "BAD ARGUMENT PARSING"
            exit 3
            ;;
    esac
done

if [ -z "${GRAFANA_BIN}"]; then
  echo "Empty --grafana-bin, using /usr/sbin/grafana-server"
  GRAFANA_BIN=/usr/sbin/grafana-server
fi
if [ -z "${GRAFANA_URL}"]; then
  echo "Empty --url"
  exit 3
fi

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
Environment=GF_SERVER_ROOT_URL=${GRAFANA_URL}
EOT

# Reset grafana-server and start it up again (or the first time)
systemctl stop grafana-server

# update any plugins while it's stopped
grafana-cli plugins update-all

systemctl daemon-reload
systemctl enable grafana-server
systemctl restart grafana-server

