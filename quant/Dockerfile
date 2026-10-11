# The quant worker (ADR-016): claims research jobs and runs them, connected as
# the quant_research role. Started with `docker compose --profile research up`.
FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /app

# A user of its own: the worker reads datasets and the database, and needs no
# privilege in the container either.
RUN useradd --create-home --uid 10001 worker

COPY pyproject.toml ./
COPY src ./src
RUN pip install --no-cache-dir .

USER worker

CMD ["python", "-m", "personal_quant.worker"]
