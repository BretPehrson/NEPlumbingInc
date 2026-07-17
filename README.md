# NEPlumbingInc

NEPlumbingInc is a Blazor web application for NE Plumbing Inc., combining a public marketing site with authenticated admin tooling for content, theme, leads, and website operations.

## Overview

This repository contains a server-rendered Blazor app (interactive server components) built on ASP.NET Core and EF Core.  
It supports customer-facing pages, lead capture, recruiting workflows, and internal management features used by admins.

## Key Features

### Public website
- Home page with editable hero/content blocks
- Services catalog with service detail modals and image delivery via API
- Contact form (`/messages`) and special-offer claim flow (`/special-offer`)
- Careers application flow (`/careers`) with optional resume upload
- NE Underground promo page (`/ne-underground`)
- Global light/dark mode support

### Admin tools
- Protected admin dashboard (`/admin-dashboard`)
- Manage home page content, services, careers content, and special offers
- Manage fonts/colors/theme tokens for light and dark palettes
- Message inbox with read/spam states and resume attachment management
- User management and website metrics dashboards
- Configurable message notification recipients

### Security and anti-spam controls
- Cookie-based admin authentication (`/auth/login`, `/auth/logout`)
- Rate limiting on form submissions
- Honeypot fields, timing-token checks, duplicate detection, sender burst protection, and spam scoring
- Resume file-size/type/signature validation before storage

## Architecture

- **UI:** Blazor components in `NEPlumbingInc/Components`
- **Backend:** ASP.NET Core controllers in `NEPlumbingInc/Controllers`
- **Data access:** EF Core SQL Server context in `NEPlumbingInc/Data/AppDbContext.cs`
- **Storage:** Azure Blob Storage services for resumes and service images (production), local service image storage in development
- **Metrics:** custom middleware logs page visits (`NEPlumbingInc/Middleware/PageVisitLoggingMiddleware.cs`)
- **Startup:** `NEPlumbingInc/Program.cs` configures DI, auth, rate limiting, controllers, Razor components, and startup migration/seed retries

## Repository Structure

- `/NEPlumbingInc` - main ASP.NET Core app
  - `Components/Pages` - public/admin pages and account views
  - `Components/Manage*.razor` - admin management modules
  - `Controllers` - form/API endpoints (contact, careers, auth, service images, offers, resume access)
  - `Services` - business logic, storage adapters, spam/email/theme/content services
  - `Data` - EF Core context, factory, seeding
  - `Migrations` - EF Core schema history
  - `wwwroot` - static assets and styles
- `docker-compose.yml` - local SQL Server container
- `.github/workflows/deploy.yml` - Azure App Service build/deploy workflow

## Prerequisites

- .NET SDK compatible with `net10.0` (see `NEPlumbingInc/NEPlumbingInc.csproj`)
- SQL Server (local instance or containerized)
- Optional: Azure Storage account for blob-backed resumes/service images
- Optional: Gmail app password for email notifications

## Local Setup

1. Clone and enter the repository:
   ```bash
   git clone https://github.com/BretPehrson/NEPlumbingInc.git
   cd NEPlumbingInc
   ```

2. Start SQL Server with Docker (recommended):
   ```bash
   cp .env.example .env
   docker compose up -d
   ```
   Update `.env` with a strong `SA_PASSWORD` before first use.

3. Configure app settings for development:
   - `NEPlumbingInc/appsettings.Development.json`
   - or user-secrets/environment variables

4. Restore and run:
   ```bash
   dotnet restore NEPlumbingInc.sln
   dotnet run --project NEPlumbingInc/NEPlumbingInc.csproj
   ```

5. Browse to the local URL from launch settings (for example `https://localhost:7162`).

## Configuration

### Database connection
`Program.cs` resolves `DefaultConnection` from several keys (first non-empty value wins), including:
- `ConnectionStrings:DefaultConnection`
- `ConnectionStrings__DefaultConnection`
- `DefaultConnection`
- `SQLAZURECONNSTR_DefaultConnection`
- `SQLCONNSTR_DefaultConnection`
- `CUSTOMCONNSTR_DefaultConnection`

### Storage settings
- `ResumeBlobStorage:ConnectionString`
- `ResumeBlobStorage:ResumeContainer` (default: `resumes`)
- `ServiceImageBlobStorage:ConnectionString`
- `ServiceImageBlobStorage:ServiceImageContainer` (default: `service-images`)
- `ServiceImageBlobStorage:LocalStoragePath` (development/local image storage path)

### Email settings
- `Email:From`
- `Email:To` (fallback recipient)
- `Email:AppPassword`

### Other notable behavior
- Startup applies EF migrations and seed logic with retries.
- A default admin login record is seeded when no login users exist.
- Development uses local file storage for service images; non-development uses blob storage service.

## Authentication

- Admin login UI: `/account/login`
- Auth endpoints: `/auth/login` and `/auth/logout`
- Admin pages require authentication (dashboard and admin APIs)

## Deployment (Azure App Service)

This repository includes a GitHub Actions workflow at `.github/workflows/deploy.yml` that:
1. Restores dependencies
2. Publishes the app
3. Packages publish output
4. Deploys to Azure App Service (`azure/webapps-deploy`)

### Required GitHub secret
- `AZURE_CREDENTIALS` (service principal JSON for `azure/login`)

### Required Azure app settings
Set production app settings for:
- database connection string (DefaultConnection)
- blob storage connection strings/containers
- email credentials (`Email__From`, `Email__To`, `Email__AppPassword`)
- any additional environment overrides used by your environment

> Note: The project targets `net10.0`, so ensure your CI/deployment SDK version matches the target framework.

## Build and Test

```bash
dotnet build NEPlumbingInc.sln
dotnet test NEPlumbingInc.sln
```

## License

No license has been specified yet.
