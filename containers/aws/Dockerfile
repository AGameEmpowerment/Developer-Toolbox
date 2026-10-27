# Local development only: free Apache-2.0 Moto built from its official PyPI package.
FROM docker.io/library/python:latest

RUN python -m pip install --no-cache-dir "moto[server]==5.2.4"

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    AWS_EC2_METADATA_DISABLED=true

WORKDIR /opt/toolbox/aws
COPY health.py init.py local_session.py run_emulator.py ./
USER 1000:1000
EXPOSE 5000
ENTRYPOINT ["python", "/opt/toolbox/aws/run_emulator.py"]
