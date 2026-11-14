# Project Instructions

- This project is a development and demo prototype for an ongoing hackathon.
- Prioritize fast feature implementation and demos on the actual device. Deployment, production readiness, and store release preparation are out of scope unless the user explicitly requests them.
- Do not require production architecture or an authentication server as prerequisites. Choose the simplest setup needed for development.
- Using development and test API keys directly in the app through local configuration is allowed. Do not commit actual keys to the repository or expose them in responses or logs.
- The target runtime is a standalone Quest 3 app. Prefer an approach that does not require a PC or Python bridge to stay running. Internet access for external AI services is acceptable.
- Split commits by feature or logical fix. Do not bundle unrelated features or changes into one large commit. Keep the files and Unity metadata required for each change together, and write a clear commit message describing that change.
- Proactively maintain AGENTS.md when the user establishes durable project preferences or when verified project conventions will help future work. Add or update these instructions without asking for separate permission, and briefly report the changes. Keep instructions concise; do not record temporary task details, secrets, or unverified assumptions as permanent rules.
