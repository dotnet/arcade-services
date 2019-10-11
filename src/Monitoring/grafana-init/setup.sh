#!/usr/bin/env bash

set -e -x

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

export DEBIAN_FRONTEND=noninteractive

add-apt-repository "deb https://packages.grafana.com/oss/deb stable main"
wget -q -O - https://packages.grafana.com/gpg.key | sudo apt-key add -

apt-get update

apt-get -y install python3-pip grafana
python3 -m pip install azure-keyvault==1.1.0 msrestazure==0.6.2

cp $DIR/vault-env.py /usr/local/bin/vault-env.py
chmod a+rx /usr/local/bin/vault-env.py

cp $DIR/grafana.ini /etc/grafana/local.ini
chown root:grafana /etc/grafana/local.ini
chmod g+r /etc/grafana/local.ini

GRAFANA_BIN=/usr/sbin/grafana-server
if [ ! -z "$1"]
then
  GRAFANA_BIN=$1
fi

mkdir -p /etc/systemd/system/grafana-server.service.d
cp $DIR/grafana-override.conf /etc/systemd/system/grafana-server.service.d/override.conf
cat <<EOT >> /etc/systemd/system/grafana-server.service.d/bin.conf
[Service]
Environment=GRAFANA_BIN=${GRAFANA_BIN}
EOT

systemctl enable grafana-server
systemctl restart grafana-server

