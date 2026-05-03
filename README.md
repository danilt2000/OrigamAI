# OrigamAI

Self-hosted RAG over the [Origam Community](https://community.origam.com) forum, built on **Microsoft Kernel Memory** with **Pollinations.AI** as the OpenAI-compatible LLM/embedding backend.

## Quick start

```bash
cd OrigamAI
dotnet run
```

Then open the **interactive API UI**:

> 👉 http://localhost:5244/scalar/v1

The root `/` redirects there. Raw OpenAPI JSON: http://localhost:5244/openapi/v1.json.

If you launch the `https` profile use `https://localhost:7035/scalar/v1` instead.

## Frontend (React + Vite)

A small SPA in `frontend/` consumes the API. It proxies `/api` to `https://localhost:7035` (the API's HTTPS profile).

```bash
cd frontend
npm install        # first time only
npm run dev        # http://localhost:5173
```


To install Olama for emmbedings 

irm https://ollama.com/install.ps1 | iex
ollama pull bge-m3
curl http://localhost:11434/api/tags
