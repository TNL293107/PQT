"""The quant worker: runs research jobs the backend queues (ADR-016, U10).

The backend writes ``research.jobs``; this process claims a job with a lease,
runs its handler and records the outcome, connected as the ``quant_research``
role — which can read ``quant`` and write only ``research``. It runs in its
own container, so a failing experiment can fail only itself.

* :mod:`~personal_quant.worker.queue` — claiming and finishing jobs in SQL.
* :mod:`~personal_quant.worker.handlers` — what each job kind runs.
* :mod:`~personal_quant.worker.runner` — the loop.

Run it with ``python -m personal_quant.worker``.
"""
