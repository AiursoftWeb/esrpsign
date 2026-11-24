#!/bin/sh
set -e
mkdir -p /var/lib/aiursoft-esrpSign
chown www-data:www-data /var/lib/aiursoft-esrpSign

# Bootstrap config if missing (matching Docker entrypoint pattern)
if [ ! -f /etc/aiursoft-esrpSign/appsettings.json ]; then
    cp /usr/share/aiursoft-esrpSign/appsettings.json /etc/aiursoft-esrpSign/appsettings.json
fi
# Replace /usr/share copy with symlink to /etc
rm -f /usr/share/aiursoft-esrpSign/appsettings.json
ln -sf /etc/aiursoft-esrpSign/appsettings.json /usr/share/aiursoft-esrpSign/appsettings.json
