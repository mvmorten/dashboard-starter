#!/usr/bin/env bash
# Prints the last lines of an App Service app's container logs, for a deploy that failed.
# Usage: app-service-logs.sh <resource group> <app name> [lines]
#
# When the app doesn't start, the deploy only says "the worker didn't start". The reason is in
# the container logs, which App Service keeps as files in /home/LogFiles. Kudu (the app's
# .scm. site, the same one `az webapp deploy` uploads to) lists and serves them. az rest signs
# in to it with the job's own Azure login: no publishing password, no new permission.
#
# Two logs matter, the newest of each:
#   *_default_docker.log  the app's own console output: exceptions, "refusing to start" messages.
#   *_docker.log          App Service's view: the container starting, failing its pings, stopping.
#
# Never fails: it's only there to explain a failure that has already happened.
set -uo pipefail

group=$1
app=$2
lines=${3:-60}
log=$(mktemp)

scm=$(az webapp show --resource-group "$group" --name "$app" \
  --query "hostNameSslStates[?hostType=='Repository'].name | [0]" --output tsv) || exit 0
kudu() { az rest --method get --url "https://$scm$1" --resource "https://management.azure.com/" "${@:2}"; }

# show <title> <JMESPath filter on the log list>: the newest matching log's last lines.
show() {
  local title=$1 filter=$2 href
  if ! href=$(kudu /api/logs/docker --query "sort_by([?$filter], &lastUpdated)[-1].href" --output tsv); then
    echo "Couldn't list the container logs of $app."
    return
  fi

  echo
  if [ -z "$href" ]; then
    echo "=== $title: no log yet ==="
    return
  fi

  # To a file: az rest prints a warning before any answer that isn't JSON.
  echo "=== $title: last $lines lines of ${href##*/} ==="
  if kudu "${href#https://"$scm"}" --output-file "$log"; then
    tail -n "$lines" "$log"
  else
    echo "Couldn't read it."
  fi
}

show "The app's console" "ends_with(path, '_default_docker.log')"
show "App Service" "ends_with(path, '_docker.log') && !ends_with(path, '_default_docker.log') && !ends_with(path, '_scm_docker.log')"
exit 0
