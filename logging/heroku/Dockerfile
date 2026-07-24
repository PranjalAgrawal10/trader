# Heroku container deploy for the logging UI.
# Bonsai Heroku sandbox is OpenSearch-compatible, so this image is
# OpenSearch Dashboards (Kibana fork) rather than Elastic Kibana 8.x.
# HTTP Basic Auth: set DASHBOARDS_BASIC_AUTH_USER / DASHBOARDS_BASIC_AUTH_PASSWORD
# on the Heroku app (do not commit secrets).
#
# From this directory:
#   heroku stack:set container -a kibana
#   heroku container:login
#   $env:DOCKER_BUILDKIT=0
#   docker build --platform linux/amd64 -t registry.heroku.com/kibana/web .
#   docker push registry.heroku.com/kibana/web
#   heroku container:release web -a kibana
FROM opensearchproject/opensearch-dashboards:2.11.1

USER root
# Drop OSD security login UI; Heroku auth-proxy.js provides username/password gate.
# Plugin CLI requires --allow-root when USER is root; also wipe the directory so a
# failed CLI remove cannot leave securityDashboards installed. Strip leftover
# opensearch_security.* keys from the stock yml (OSD fatals on unknown keys).
RUN /usr/share/opensearch-dashboards/bin/opensearch-dashboards-plugin remove securityDashboards --allow-root \
  ; rm -rf /usr/share/opensearch-dashboards/plugins/securityDashboards \
  && test ! -d /usr/share/opensearch-dashboards/plugins/securityDashboards \
  && sed -i '/^opensearch_security\./d' /usr/share/opensearch-dashboards/config/opensearch_dashboards.yml \
  && ! grep -q '^opensearch_security\.' /usr/share/opensearch-dashboards/config/opensearch_dashboards.yml

COPY entrypoint.sh /usr/share/opensearch-dashboards/heroku-entrypoint.sh
COPY auth-proxy.js /usr/share/opensearch-dashboards/auth-proxy.js
RUN chmod +x /usr/share/opensearch-dashboards/heroku-entrypoint.sh \
  && sed -i 's/\r$//' /usr/share/opensearch-dashboards/heroku-entrypoint.sh \
  && chown opensearch-dashboards:opensearch-dashboards \
    /usr/share/opensearch-dashboards/heroku-entrypoint.sh \
    /usr/share/opensearch-dashboards/auth-proxy.js

USER opensearch-dashboards
ENTRYPOINT ["/usr/share/opensearch-dashboards/heroku-entrypoint.sh"]
