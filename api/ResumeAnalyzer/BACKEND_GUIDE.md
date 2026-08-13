# Backend Guide — ResumeAnalyzer API

ASP.NET Core 10 Clean Architecture (Jason Taylor template), PostgreSQL, MediatR CQRS.
The template's sample code (Todo, WeatherForecasts, Users) has been removed — `src/Web/Endpoints/` is empty and waiting for your features.

---

## 1. Quick start

```bash
# from repo root
docker compose up -d

# then
cd api/ResumeAnalyzer/src/Web
dotnet watch run
```

| Thing | URL |
|---|---|
| API | http://localhost:5003 |
| Swagger UI | http://localhost:5003/swagger |
| Scalar | http://localhost:5003/scalar |
| OpenAPI spec | http://localhost:5003/openapi/v1.json |
| Seq (logs) | http://localhost:8081 |
| Postgres | `localhost:5433` |

> `dotnet watch run` must be run from `src/Web`, not the solution root — the root has multiple runnable projects.

**First-time setup on a new machine:**

```bash
cp .env.example .env          # then edit POSTGRES_PASSWORD
cd api/ResumeAnalyzer/src/Web
dotnet user-secrets set "ConnectionStrings:ResumeAnalyzerDb" \
  "Server=127.0.0.1;Port=5433;Database=ResumeAnalyzerDb;Username=admin;Password=<yours>"
```

---

## 2. The projects

```
Domain  ←  Application  ←  Infrastructure  ←  Web
                                ↖ ServiceDefaults ↗
Shared (constants) · AppHost (Aspire, optional)
```

Arrows point **toward** dependencies. Domain depends on nothing. Never reverse an arrow — the compiler will refuse anyway, since it would be circular.

| Project | Purpose | Edit frequency |
|---|---|---|
| **Domain** | Entities, value objects, enums, domain events. Zero framework code. | Often |
| **Application** | Use cases — commands, queries, handlers, interfaces. | Constantly |
| **Infrastructure** | EF Core, Identity, external APIs, file/blob storage. | Occasionally |
| **Web** | HTTP endpoints only. Thin. | Per feature |
| **Shared** | Aspire resource-name constants (`Services.Database`). | Rarely |
| **ServiceDefaults** | OpenTelemetry, health checks, resilience. | Never |
| **AppHost** | Aspire orchestration (alternate run path). | Never |

### Why interfaces live in Application

`Application.csproj` references **only** Domain. It cannot reference Infrastructure — Infrastructure already references Application, so the reverse is circular.

So when a handler needs a database or an external API, Application declares an **interface it owns**, and Infrastructure implements it. This is dependency inversion: instead of Application depending on the database, the database depends on Application's contract.

What it buys you:

1. **Compile-time guardrail** — you physically cannot call Npgsql or an HTTP client from a handler.
2. **Testable handlers** — mock the interface, no database needed.
3. **Swappable implementations** — change the class in Infrastructure, Application is untouched.

> Note: the template is pragmatic, not purist. `IApplicationDbContext` exposes `DbSet<T>` (an EF Core type), so Application does reference EF Core. That's a deliberate trade to keep LINQ and `ProjectTo` available in handlers.

---

## 3. Folder reference

### `src/Domain` — no dependencies

| Folder | Contents |
|---|---|
| `Entities/` | Database tables. Inherit `BaseAuditableEntity`. |
| `ValueObjects/` | Immutable, no ID, compared by value. Inherit `ValueObject`. |
| `Enums/` | Enums. |
| `Events/` | Domain events — inherit `BaseEvent`. |
| `Exceptions/` | Domain rule violations. |
| `Constants/` | `Roles.cs`, policy names. |
| `Common/` | `BaseEntity`, `BaseAuditableEntity`, `BaseEvent`, `ValueObject`. **Don't edit.** |

⚠️ `Entities/`, `Enums/`, `Events/` are currently empty, so those namespaces don't exist. **Uncomment the matching line in `src/Domain/GlobalUsings.cs` when you add the first file to each folder.**

### `src/Application` — organised by feature, not by type

```
src/Application/
  Resumes/                                  ← one folder per feature
    Commands/
      CreateResume/
        CreateResume.cs                     ← command + handler, ONE file
        CreateResumeCommandValidator.cs
    Queries/
      GetResumes/
        GetResumes.cs                       ← query + handler
        ResumeDto.cs
    EventHandlers/
      ResumeAnalyzedEventHandler.cs
  Common/                                   ← cross-feature only
    Interfaces/                             ← ALL service contracts
    Behaviours/                             ← 5 pipeline behaviours. Don't edit.
    Exceptions/
    Models/                                 ← Result, LookupDto
    Security/                               ← AuthorizeAttribute
```

Rule: used by one feature → that feature's folder. Used by two or more → `Common/`.

### `src/Infrastructure`

| Folder | Contents |
|---|---|
| `Data/` | `ApplicationDbContext`, `ApplicationDbContextInitialiser` (seeding) |
| `Data/Configurations/` | `IEntityTypeConfiguration<T>` — auto-applied |
| `Data/Interceptors/` | Audit stamping, domain event dispatch. Don't edit. |
| `Identity/` | `ApplicationUser`, `IdentityService` |
| `Migrations/` | *Doesn't exist yet* — created by `dotnet ef migrations add` |
| *(add as needed)* | `Ai/`, `Files/`, `Storage/` — group by concern |

### `src/Web`

| Folder | Contents |
|---|---|
| `Endpoints/` | One class per resource, implements `IEndpointGroup`. **Currently empty.** |
| `Services/` | Web-context services — `CurrentUser` (reads HTTP claims) |
| `Infrastructure/` | Endpoint discovery, OpenAPI transformers, exception handler. Don't edit. |
| `wwwroot/` | Static files |

---

## 4. Where does X go?

| Writing… | Path |
|---|---|
| Command | `Application/{Feature}/Commands/{Name}/{Name}.cs` |
| Validator | same folder, `{Name}CommandValidator.cs` |
| Query | `Application/{Feature}/Queries/{Name}/{Name}.cs` |
| DTO | same folder as its query |
| Domain event handler | `Application/{Feature}/EventHandlers/` |
| **Service interface** | `Application/Common/Interfaces/` |
| **Service implementation** | `Infrastructure/{Concern}/` |
| Entity | `Domain/Entities/` |
| Value object / enum / event | `Domain/ValueObjects/`, `Enums/`, `Events/` |
| EF entity config | `Infrastructure/Data/Configurations/` |
| Endpoint | `Web/Endpoints/{Resource}.cs` |
| Unit test | `tests/Domain.UnitTests/` |
| Handler test (real DB) | `tests/Application.FunctionalTests/{Feature}/` |

---

## 5. Request flow

```
POST /api/Resumes
  → Web/Endpoints/Resumes.cs          knows HTTP
  → ISender.Send(command)
  → MediatR pipeline:
       LoggingBehaviour                logs request + user
       UnhandledExceptionBehaviour     catch-all logging
       AuthorizationBehaviour          reads [Authorize] → 401/403
       ValidationBehaviour             runs validators → 400
       PerformanceBehaviour            warns on slow requests
  → CreateResumeCommandHandler         knows only interfaces
  → IApplicationDbContext              ← the boundary
  → ApplicationDbContext               knows EF Core   [Infrastructure]
  → Postgres
```

Everything above the boundary is pure C# you can unit test. The five behaviours are registered in `Application/DependencyInjection.cs` and run on **every** request — you never invoke them yourself.

---

## 6. Writing a feature

### 6.1 Entity

```csharp
// src/Domain/Entities/Resume.cs
namespace ResumeAnalyzer.Domain.Entities;

public class Resume : BaseAuditableEntity
{
    public string OwnerId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string RawText { get; set; } = null!;
    public ResumeStatus Status { get; set; } = ResumeStatus.Pending;
}
```

`BaseAuditableEntity` gives `Id` + `Created`/`CreatedBy`/`LastModified`/`LastModifiedBy`, stamped automatically by `AuditableEntityInterceptor`. Use plain `BaseEntity` to skip the audit columns. Both provide `AddDomainEvent()`.

Then **uncomment** `global using ResumeAnalyzer.Domain.Entities;` in `src/Domain/GlobalUsings.cs`.

### 6.2 Register the DbSet — two places

```csharp
// src/Application/Common/Interfaces/IApplicationDbContext.cs
public interface IApplicationDbContext
{
    DbSet<Resume> Resumes { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
```

```csharp
// src/Infrastructure/Data/ApplicationDbContext.cs
public DbSet<Resume> Resumes => Set<Resume>();
```

### 6.3 EF configuration (optional)

```csharp
// src/Infrastructure/Data/Configurations/ResumeConfiguration.cs
public class ResumeConfiguration : IEntityTypeConfiguration<Resume>
{
    public void Configure(EntityTypeBuilder<Resume> builder)
    {
        builder.Property(r => r.FileName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.OwnerId).IsRequired();
        builder.HasIndex(r => r.OwnerId);
    }
}
```

Auto-discovered via `ApplicationDbContext.OnModelCreating` — no registration.

### 6.4 Command + handler (ONE file)

```csharp
// src/Application/Resumes/Commands/CreateResume/CreateResume.cs
using ResumeAnalyzer.Application.Common.Interfaces;
using ResumeAnalyzer.Application.Common.Security;
using ResumeAnalyzer.Domain.Entities;

namespace ResumeAnalyzer.Application.Resumes.Commands.CreateResume;

[Authorize]
public record CreateResumeCommand : IRequest<int>
{
    public string FileName { get; init; } = null!;
    public string RawText { get; init; } = null!;
}

public class CreateResumeCommandHandler(IApplicationDbContext context, IUser user)
    : IRequestHandler<CreateResumeCommand, int>
{
    public async Task<int> Handle(CreateResumeCommand request, CancellationToken ct)
    {
        var entity = new Resume
        {
            OwnerId  = user.Id!,
            FileName = request.FileName,
            RawText  = request.RawText
        };

        context.Resumes.Add(entity);
        await context.SaveChangesAsync(ct);

        return entity.Id;
    }
}
```

Keep the handler in the same file as the command — that's the template convention.

`[Authorize]` here is `ResumeAnalyzer.Application.Common.Security.AuthorizeAttribute`, **not** ASP.NET's. It supports:

```csharp
[Authorize]
[Authorize(Roles = "Administrator")]
[Authorize(Policy = "CanDeleteResumes")]
```

### 6.5 Validator

```csharp
// same folder — CreateResumeCommandValidator.cs
public class CreateResumeCommandValidator : AbstractValidator<CreateResumeCommand>
{
    public CreateResumeCommandValidator()
    {
        RuleFor(v => v.FileName).NotEmpty().MaximumLength(200);
        RuleFor(v => v.RawText).NotEmpty();
    }
}
```

No registration. `AddValidatorsFromAssembly` finds it, `ValidationBehaviour` runs it, failures become a **400** with per-field errors. **Never call a validator yourself.**

### 6.6 Query + DTO

```csharp
// src/Application/Resumes/Queries/GetResumes/GetResumes.cs
[Authorize]
public record GetResumesQuery : IRequest<IReadOnlyList<ResumeDto>>;

public class GetResumesQueryHandler(IApplicationDbContext context, IUser user, IMapper mapper)
    : IRequestHandler<GetResumesQuery, IReadOnlyList<ResumeDto>>
{
    public async Task<IReadOnlyList<ResumeDto>> Handle(GetResumesQuery request, CancellationToken ct)
        => await context.Resumes
            .AsNoTracking()
            .Where(r => r.OwnerId == user.Id)        // always filter by owner
            .ProjectTo<ResumeDto>(mapper.ConfigurationProvider)
            .OrderByDescending(r => r.Created)
            .ToListAsync(ct);
}
```

```csharp
// ResumeDto.cs — same folder
public class ResumeDto
{
    public int Id { get; init; }
    public string FileName { get; init; } = null!;
    public DateTimeOffset Created { get; init; }

    private class Mapping : Profile
    {
        public Mapping() => CreateMap<Resume, ResumeDto>();
    }
}
```

`ProjectTo` pushes the projection into SQL so only DTO columns are selected. A hand-written `.Select(r => new ResumeDto { ... })` is equally efficient and easier to debug — AutoMapper is optional here.

### 6.7 Endpoint

```csharp
// src/Web/Endpoints/Resumes.cs
using ResumeAnalyzer.Application.Resumes.Commands.CreateResume;
using ResumeAnalyzer.Application.Resumes.Queries.GetResumes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ResumeAnalyzer.Web.Endpoints;

public class Resumes : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();

        groupBuilder.MapGet(GetResumes);
        groupBuilder.MapPost(CreateResume);
        groupBuilder.MapDelete(DeleteResume, "{id}");
    }

    [EndpointSummary("List resumes")]
    [EndpointDescription("Returns the current user's resumes.")]
    public static async Task<Ok<IReadOnlyList<ResumeDto>>> GetResumes(ISender sender)
        => TypedResults.Ok(await sender.Send(new GetResumesQuery()));

    [EndpointSummary("Create a resume")]
    public static async Task<Created<int>> CreateResume(ISender sender, CreateResumeCommand command)
    {
        var id = await sender.Send(command);
        return TypedResults.Created($"/api/{nameof(Resumes)}/{id}", id);
    }

    [EndpointSummary("Delete a resume")]
    public static async Task<NoContent> DeleteResume(ISender sender, int id)
    {
        await sender.Send(new DeleteResumeCommand(id));
        return TypedResults.NoContent();
    }
}
```

**No registration.** `Program.cs` line 52 (`MapEndpoints`) reflects over the assembly for `IEndpointGroup`. The class name becomes the route: `/api/Resumes`. Override with:

```csharp
public static string RoutePrefix => "/api/custom/path";
```

**Endpoint rules:**

- Handlers must be **named static methods** — `Guard.Against.AnonymousMethod` rejects lambdas, because the method name becomes the OpenAPI `operationId`.
- `pattern` is **optional** for `MapGet`/`MapPost`, **required** for `MapPut`/`MapPatch`/`MapDelete`.
- Return `Results<Ok<T>, BadRequest>` unions so OpenAPI documents every outcome.

### 6.8 File uploads

```csharp
groupBuilder.MapPost(UploadResume).DisableAntiforgery();

public static async Task<Created<int>> UploadResume(ISender sender, IFormFile file)
{
    await using var stream = file.OpenReadStream();
    var id = await sender.Send(new UploadResumeCommand { FileName = file.FileName, Content = stream });
    return TypedResults.Created($"/api/Resumes/{id}", id);
}
```

⚠️ **`.DisableAntiforgery()` is required.** Minimal APIs enable antiforgery validation on `IFormFile` endpoints by default; without it uploads fail with a 400 before reaching your handler.

---

## 7. Errors — throw, don't return

Handlers throw; `ProblemDetailsExceptionHandler` maps to HTTP:

| Exception | Status |
|---|---|
| `ValidationException` (from FluentValidation) | 400 + field errors |
| `NotFoundException` (from `Ardalis.GuardClauses`) | 404 |
| `UnauthorizedAccessException` | 401 |
| `ForbiddenAccessException` | 403 |

```csharp
var entity = await context.Resumes.FindAsync([request.Id], ct);
Guard.Against.NotFound(request.Id, entity);   // → 404, no plumbing needed
```

Never return status codes from a handler — that's the endpoint's job.

---

## 8. Services (Infrastructure)

A service belongs in Infrastructure if it **does I/O or wraps a third-party library**. If it's a pure decision or calculation, it belongs in Domain or the handler.

| Category | Example interface |
|---|---|
| Data access | `IApplicationDbContext` ✅ exists |
| Identity | `IIdentityService` ✅ exists |
| Clock | `TimeProvider` ✅ exists |
| File storage | `IFileStorage` |
| Document parsing | `IDocumentParser` |
| External HTTP APIs | `IPaymentGateway`, analysis clients |
| Email / SMS | `IEmailSender` |
| Caching | `ICacheService` |
| Background jobs | `IJobScheduler` |

### Three steps, every time

```csharp
// 1. Contract — src/Application/Common/Interfaces/IDocumentParser.cs
public interface IDocumentParser
{
    Task<string> ExtractTextAsync(Stream content, CancellationToken ct);
}
```

```csharp
// 2. Implementation — src/Infrastructure/Documents/PdfDocumentParser.cs
public class PdfDocumentParser : IDocumentParser
{
    public async Task<string> ExtractTextAsync(Stream content, CancellationToken ct) { /* ... */ }
}
```

```csharp
// 3. Register — src/Infrastructure/DependencyInjection.cs
builder.Services.AddScoped<IDocumentParser, PdfDocumentParser>();
```

### Consuming it

```csharp
public class UploadResumeCommandHandler(
    IApplicationDbContext context,
    IDocumentParser parser)                 // ← just ask for it
    : IRequestHandler<UploadResumeCommand, int>
{
    public async Task<int> Handle(UploadResumeCommand request, CancellationToken ct)
    {
        var text = await parser.ExtractTextAsync(request.Content, ct);
        // ...
    }
}
```

You never write `new PdfDocumentParser()`. Miss step 3 and you get a runtime error: *"Unable to resolve service for type IDocumentParser."*

### Lifetimes

| Lifetime | Meaning | Use for |
|---|---|---|
| `AddScoped` | One per HTTP request | **Default.** DbContext, parsers, most services |
| `AddSingleton` | One for app lifetime | Stateless/expensive — `TimeProvider`, config |
| `AddTransient` | New every injection | Cheap stateless helpers |

⚠️ Never inject a scoped service into a singleton.

### Exception: implementations live where their dependency lives

`IUser` is declared in Application but implemented in **Web** (`Web/Services/CurrentUser.cs`) because it needs `HttpContext`. Infrastructure is the default home, not the only one.

---

## 9. EF Core migrations

### Current state — migrations are NOT in use

`ApplicationDbContextInitialiser.cs` currently calls:

```csharp
await _context.Database.EnsureDeletedAsync();
await _context.Database.EnsureCreatedAsync();
```

**Every startup in Development drops and recreates the database.** With `dotnet watch`, that means every hot restart wipes your data. Fine for early prototyping, painful once you have real data.

### Switching to migrations

**Step 1 — fix the CLI version.** The project uses EF Core **10.0.5**; a `dotnet-ef` older than that will fail.

```bash
dotnet ef --version                # check
dotnet tool update --global dotnet-ef
```

**Step 2 — create the first migration** (run from `api/ResumeAnalyzer/`):

```bash
dotnet ef migrations add InitialCreate \
  --project src/Infrastructure \
  --startup-project src/Web
```

`--project` is where migrations are written; `--startup-project` is where the connection string lives.

**Step 3 — replace the initialiser** in `ApplicationDbContextInitialiser.InitialiseAsync()`:

```csharp
await _context.Database.MigrateAsync();
```

### Everyday commands

```bash
# from api/ResumeAnalyzer/

# add a migration after changing entities
dotnet ef migrations add AddResumeStatus --project src/Infrastructure --startup-project src/Web

# apply pending migrations manually
dotnet ef database update --project src/Infrastructure --startup-project src/Web

# roll back to a specific migration
dotnet ef database update InitialCreate --project src/Infrastructure --startup-project src/Web

# remove the last (unapplied) migration
dotnet ef migrations remove --project src/Infrastructure --startup-project src/Web

# list migrations
dotnet ef migrations list --project src/Infrastructure --startup-project src/Web

# preview the SQL without running it
dotnet ef migrations script --project src/Infrastructure --startup-project src/Web
```

> Once `MigrateAsync()` is in place, migrations apply automatically at startup — `database update` is only needed for manual control.

---

## 10. Docker setup

`docker-compose.yml` lives at the **repo root** (not in `api/`).

```bash
docker compose up -d          # start postgres + seq
docker compose ps             # status
docker compose logs -f postgres
docker compose down           # stop (keeps data)
docker compose down -v        # stop AND delete volumes — wipes the database
```

### Services

| Service | Host port | Notes |
|---|---|---|
| `postgres` (17-alpine) | **5433** | Not 5432 — see below |
| `seq` | 8081 (UI), 5341 (ingest) | Structured log viewer |

### Two gotchas already handled

1. **Port 5433, not 5432.** A native `postgresql-x64-18` Windows service occupies 5432 on this machine. The container maps to 5433 to avoid the clash; the connection string matches.
2. **`SEQ_FIRSTRUN_NOAUTHENTICATION: "Y"`** is required — current Seq images refuse to start without an admin password or an explicit opt-out.

### Credentials come from `.env`

```yaml
POSTGRES_USER: ${POSTGRES_USER:?set POSTGRES_USER in .env}
POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}
```

The `:?` syntax makes compose **fail loudly** if the variable is missing rather than starting with a blank user.

- `.env` — real values, **gitignored**
- `.env.example` — committed template

> Seq will stay empty until logs are shipped to it. The app produces OpenTelemetry logs already, so setting `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:5341/ingest/otlp` in `launchSettings.json` should route them there (untested). Otherwise add a Serilog sink.

---

## 11. Configuration & secrets

Config sources, in ascending priority:

1. `appsettings.json` — committed, **no secrets**
2. `appsettings.{Environment}.json`
3. **User secrets** — Development only, stored outside the repo
4. Environment variables
5. Command-line args

The connection string lives in **user secrets**, not `appsettings.json`:

```bash
cd api/ResumeAnalyzer/src/Web
dotnet user-secrets list
dotnet user-secrets set "ConnectionStrings:ResumeAnalyzerDb" "Server=127.0.0.1;Port=5433;..."
```

Stored at `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`.

⚠️ `.env` (Docker) and user secrets (API) are **two separate stores**. Change the password in one and you must change it in the other.

### Adding NuGet packages

Central Package Management is enabled — **all versions live in `Directory.Packages.props`**, and `.csproj` files reference packages without a version. Never hand-edit a `.csproj`:

```bash
dotnet add src/Web/Web.csproj package SomePackage    # updates both files correctly
```

---

## 12. Authentication & authorization

**Not JWT. Not cookies.** `Infrastructure/DependencyInjection.cs`:

```csharp
builder.Services.AddAuthentication()
    .AddBearerToken(IdentityConstants.BearerScheme);
```

This is ASP.NET Core's **BearerToken** handler (.NET 8+). Tokens are **opaque** — encrypted with Data Protection, not signed JWTs. Clients send `Authorization: Bearer <token>`; nothing is stored in a cookie.

Consequences for the frontend:

- The token **cannot be decoded** client-side → you need a `GET /api/Users/me` endpoint for user info
- Storage is your choice (`localStorage`, memory) — no httpOnly cookie
- Every request needs the header attached manually
- Tokens expire in **3600s** → implement 401 → refresh → retry

### Two authorization layers

```csharp
groupBuilder.RequireAuthorization();     // 1. HTTP — 401 before the handler runs

[Authorize(Roles = "Administrator")]     // 2. Application — on the command record
public record DeleteResumeCommand : IRequest;
```

Layer 2 protects the *use case*, so a command dispatched from a background job is still checked.

### Ownership — this is on you

Identity says *who* the caller is; nothing enforces *what they own*. Inject `IUser`, stamp on write, filter on read:

```csharp
var entity = new Resume { OwnerId = user.Id! };                    // write
await context.Resumes.Where(r => r.OwnerId == user.Id).ToListAsync(ct);   // read
```

### Current gaps

- **No auth endpoints exist** — the template's `Users.cs` was removed. You must write register/login/refresh/me.
  `SignInManager` is registered (via `.AddApiEndpoints()`); issue tokens with
  `TypedResults.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme)`.
- **No `IEmailSender`** registered — password reset / email confirmation will silently do nothing.
- **Data Protection keys are not persisted.** Tokens become invalid on restart or across instances. Before deploying, add:
  ```csharp
  services.AddDataProtection()
      .SetApplicationName("ResumeAnalyzer")
      .PersistKeysToDbContext<ApplicationDbContext>();
  ```

Seeded dev user: `administrator@localhost` / `Administrator1!`

---

## 13. Testing

| Project | Purpose | Needs Docker |
|---|---|---|
| `Domain.UnitTests` | Pure domain logic | No |
| `Application.UnitTests` | Behaviours, mappings | No |
| `Application.FunctionalTests` | Handlers against a real database | **Yes** |
| `Infrastructure.IntegrationTests` | Infrastructure implementations | Yes |

```bash
dotnet test                                              # everything
dotnet test tests/Domain.UnitTests                       # one project
```

Functional tests spin up their own Aspire AppHost (`tests/TestAppHost`) which starts a throwaway Postgres container, then use **Respawn** to reset tables between tests (`TestBase` calls `TestApp.ResetState()`).

Mirror the feature path: `tests/Application.FunctionalTests/Resumes/Commands/CreateResumeTests.cs`.

Stack: **NUnit** + **Shouldly** (`result.ShouldBe(1)`) + **Moq**.

---

## 14. Aspire

Aspire appears in four places; only the first is optional.

| Piece | Used by `dotnet watch run`? |
|---|---|
| **AppHost** (`src/AppHost`) | ❌ replaced by docker-compose |
| **ServiceDefaults** | ✅ every run — OTel, health checks, resilience |
| **Aspire.Npgsql enrichment** | ✅ every run — retries, health check, EF telemetry |
| **TestAppHost** | ✅ whenever functional tests run |

Alternate run path (starts Postgres itself + dashboard):

```bash
dotnet run --project api/ResumeAnalyzer/src/AppHost
```

Don't run both flows at once — you'd get two separate databases.

`src/Shared/Services.cs` is the glue: `Services.Database` (`"ResumeAnalyzerDb"`) is simultaneously the Aspire resource name and the connection-string key, which is why swapping flows needs no code change.

---

## 15. Conventions & gotchas

- **Command + handler in the same file.** Don't split them.
- **Records for commands/queries/DTOs** (`init` properties), **classes for entities**.
- **Throw in handlers, return status codes in endpoints.**
- **Handlers depend only on interfaces** — never on Infrastructure types.
- **Always filter queries by `user.Id`** unless the data is genuinely public.
- `[Authorize]` is ambiguous — `Application.Common.Security` on commands, ASP.NET's `RequireAuthorization()` on route groups.
- **No lambdas in `Map*`** — named static methods only.
- **Uncomment the `global using`** when adding the first type to `Domain/Entities`, `Enums`, or `Events`.
- **`UseExceptionHandler(options => { })`** — the empty lambda is required; removing it silently disables ProblemDetails mapping.
- **`UseAuthentication`/`UseAuthorization` are absent from `Program.cs` on purpose** — `WebApplication` adds them automatically when the services are registered.
- **`NuGetAudit` is disabled** in `Directory.Build.props`. Transitive packages have known advisories (`System.Security.Cryptography.Xml`, `MessagePack`, `OpenTelemetry.*`); bump them before production.
- **CORS is wide open** (`AllowAnyOrigin`) — tighten before deploying.

---

## 16. Best practices

### 16.1 Queries

**Always `AsNoTracking()` on reads.** Change tracking costs memory and time, and read queries never need it.

**Never return an unbounded list.** A `GetResumes` that works with 10 rows dies at 100,000. Paginate from day one:

```csharp
public record GetResumesQuery : IRequest<PaginatedList<ResumeDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;      // always have a default
}
```

Validate `PageSize` with an upper bound (`RuleFor(x => x.PageSize).InclusiveBetween(1, 100)`) — otherwise a client sends `pageSize=1000000` and you've built your own DoS.

**Project, don't load.** `ProjectTo`/`Select` generates `SELECT id, file_name` instead of `SELECT *`. Loading a full entity to map three fields wastes I/O — and pulls columns like `RawText` you probably don't want in a list view.

**Watch for N+1.** This runs one query per resume:

```csharp
var resumes = await context.Resumes.ToListAsync(ct);
foreach (var r in resumes)
    var count = r.Analyses.Count;      // lazy load per row — N+1
```

Project the count in the same query, or `Include()` deliberately. If a list endpoint gets slow, this is the first thing to check.

**Filter before you materialise.** `.Where()` before `.ToListAsync()` runs in SQL; after it, in memory over the whole table.

### 16.2 Commands

**One `SaveChangesAsync` per handler**, at the end. Multiple calls mean partial writes if the second fails. EF batches everything in one transaction when you save once.

**Handlers do one thing.** If a handler is 100 lines, it's probably several use cases. Split it, or move logic into the entity.

**Put invariants in the entity, not the handler.** "A resume can't be analysed twice" is a domain rule — enforce it on `Resume`, so it holds no matter which handler runs:

```csharp
public void MarkAnalyzed()
{
    if (Status == ResumeStatus.Analyzed)
        throw new ResumeAlreadyAnalyzedException(Id);

    Status = ResumeStatus.Analyzed;
    AddDomainEvent(new ResumeAnalyzedEvent(this));
}
```

Validators check *shape* (not empty, max length). Entities enforce *rules* (state transitions, business logic). Don't put business rules in validators — they only run on the way in.

### 16.3 Async

- **Pass `CancellationToken` everywhere.** Every async call in a handler should receive it. When a client disconnects, the work should stop — without it, you keep burning DB connections on abandoned requests.
- **Never `.Result` or `.Wait()`.** Deadlocks and thread-pool starvation. Always `await`.
- **No `async void`** except event handlers — exceptions in them crash the process.
- **Don't `async`/`await` a single pass-through call.** `=> context.Resumes.ToListAsync(ct)` without `async` is fine and skips a state machine.
- `ConfigureAwait(false)` is **not** needed in ASP.NET Core — there's no synchronization context.

### 16.4 Security

**Never trust an ID from the client.** `DELETE /api/Resumes/5` must verify the caller owns resume 5. Checking `[Authorize]` only proves they're *someone*:

```csharp
var entity = await context.Resumes
    .FirstOrDefaultAsync(r => r.Id == request.Id && r.OwnerId == user.Id, ct);
Guard.Against.NotFound(request.Id, entity);      // 404, not 403 — don't leak existence
```

Returning 404 rather than 403 avoids confirming that someone else's resume exists.

**Never return entities from endpoints.** Always a DTO. Entities leak columns you didn't mean to expose (`OwnerId`, internal flags) and couple your API shape to your schema, so a column rename becomes a breaking API change.

**Don't bind commands straight from untrusted input for privileged fields.** If `Resume` ever gets an `IsVerified` flag, a client posting `{"isVerified": true}` sets it — because the command record binds it. Keep privileged fields off commands entirely.

**Never log secrets or PII.** `LoggingBehaviour` logs the whole request object (`{@Request}`). A command containing a password or full resume text ends up in Seq. Override `ToString()` on sensitive commands or exclude them.

**Add rate limiting before production** — `UseRateLimiter` in `Program.cs`, especially on login and upload endpoints.

### 16.5 Migrations

- **Name them meaningfully.** `AddResumeStatus`, not `Migration2`. You'll read this list in a year.
- **Read the generated migration before applying it.** EF sometimes decides a rename is a drop-and-recreate — which silently deletes data. `dotnet ef migrations script` shows the SQL.
- **Never edit a migration that's been applied anywhere else.** Add a new one instead.
- **One migration per logical change.** Easier to roll back and review.
- **Applied migrations are history** — don't rewrite them once shared.

### 16.6 Concurrency

Two users editing the same row: last write silently wins and the first user's changes vanish. For anything users edit concurrently, add optimistic concurrency. On Postgres, Npgsql can use the built-in `xmin` system column as a concurrency token:

```csharp
builder.UseXminAsConcurrencyToken();     // in your IEntityTypeConfiguration
```

Then a stale update throws `DbUpdateConcurrencyException` instead of quietly overwriting. Not needed for append-only data.

### 16.7 Indexes

EF creates indexes for foreign keys, but **not** for columns you filter on. If every query does `.Where(r => r.OwnerId == user.Id)`, that column needs an index:

```csharp
builder.HasIndex(r => r.OwnerId);
```

Add them as you write the queries, not after production slows down.

### 16.8 Logging

- **Structured, not interpolated.** `_logger.LogInformation("Resume {ResumeId} analyzed", id)` — not `$"Resume {id} analyzed"`. The first is queryable in Seq; the second is a flat string.
- **Log at the boundaries**, not every line. The pipeline behaviours already log every request, timing, and unhandled exception.
- **Log exceptions with the exception object**: `_logger.LogError(ex, "...")` — passing only `ex.Message` loses the stack trace.

### 16.9 Testing

- **Test handlers, not endpoints.** Endpoints are 3 lines of plumbing; the logic is in the handler.
- **One behaviour per test**, named for what it asserts: `ShouldThrowWhenFileNameEmpty`.
- **Test the failure paths** — validation rejections, missing entities, ownership violations. Those are where bugs live.
- **Don't mock what you own.** Mock `IDocumentParser` (external I/O); use the real database for `IApplicationDbContext` via functional tests.

### 16.10 General

- **Delete dead code** rather than commenting it out — git remembers.
- **Nullable annotations are documentation.** `string?` means "genuinely can be null." Don't spray `!` to silence warnings; it defeats the point.
- **Keep `Program.cs` boring.** New services go in a layer's `DependencyInjection.cs`; new endpoints are auto-discovered.
- **`DateTimeOffset` over `DateTime`** for anything timestamped — it carries the offset and survives timezone changes.
- **Inject `TimeProvider`** instead of calling `DateTime.UtcNow`. It's already registered, and it makes time-dependent logic testable.
