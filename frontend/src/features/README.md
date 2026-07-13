# features

One folder per business module, each self-contained:

```
features/<module>/
├── api.ts          # calls into lib/api-client for this module's endpoints
├── types.ts        # request/response types mirroring the API contracts
├── components/     # module-specific UI
└── hooks/          # module-specific state
```

Routes in `src/app/` stay thin: they compose feature components and handle layout.
Modules are added per docs/development-plan.md — none exist yet.
