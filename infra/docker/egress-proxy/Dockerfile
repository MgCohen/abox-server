# The egress sidecar (ADR 0013): a CONNECT proxy that allowlists the agent box's only
# route out. Built as `abox-egress-proxy:latest`; brought up once by egress-up.sh.
FROM python:3.12-slim
COPY allowlist-proxy.py /allowlist-proxy.py
ENV ALLOW=api.anthropic.com PORT=8888
EXPOSE 8888
ENTRYPOINT ["python3", "/allowlist-proxy.py"]
