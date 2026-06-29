# Progressive Commitment Harness

Progressive Commitment Harness is a time-and-commitment planning engine. A strong model expands the option space, a typed harness owns state and approvals, and a small local model receives only compiled stage-local packets so it can emit forms, choices, summaries, or approval requests without carrying the whole trip in context.

Travel in one country is the first proving ground, but the core abstraction is a trip with commitments: vacation, business travel, funeral logistics, family support, medical/admin travel, or mixed-purpose downtime all flow through the same contracts.

## Current Shape

- Primary branch: `main`
- Remote: `git@github.com:walkingIssue/progressive-commitment-harness.git`
- Local coordination board: `C:\Users\Bartek\Documents\Playground\progressive-commitment-harness-agent-comms` (not committed)
- UI shell: ASP.NET Core Blazor Web App with Interactive Server render mode and TypeScript browser modules

## Repo Layout

```text
docs/
  PLAN.md                 staged build plan and feasibility gates
  architecture/           contract, projection, UI, and authority notes
src/
  Pch.UI/                 Blazor Web App shell for end-to-end UI testing
```

## Local UI Smoke

```powershell
cd C:\Users\Bartek\Documents\Playground\progressive-commitment-harness
cd .\src\Pch.UI
npm install
npm run build:ui
cd ..\..
dotnet build
dotnet run --project .\src\Pch.UI\Pch.UI.csproj
```

The UI is intentionally a feasibility shell, not final product design. End-to-end tests should pass through this surface once the harness API exists.
