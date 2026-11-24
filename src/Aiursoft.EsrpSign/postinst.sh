#!/bin/sh
set -e
mkdir -p /var/lib/aiursoft-esrpsign
chown www-data:www-data /var/lib/aiursoft-esrpsign

# Bootstrap config if missing (matching Docker entrypoint pattern)
if [ ! -f /etc/aiursoft-esrpsign/appsettings.json ]; then
    cp /usr/share/aiursoft-esrpsign/appsettings.json /etc/aiursoft-esrpsign/appsettings.json
fi
# Replace /usr/share copy with symlink to /etc
rm -f /usr/share/aiursoft-esrpsign/appsettings.json
ln -sf /etc/aiursoft-esrpsign/appsettings.json /usr/share/aiursoft-esrpsign/appsettings.json
