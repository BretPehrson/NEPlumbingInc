# NEPlumbingInc

NEPlumbingInc is a Blazor-based website for a local plumbing company, built with C# and hosted on Azure.

The site includes public pages for customers, a careers section, and admin tools for managing content, colors, messages, and website metrics.

## Overview

This application is designed to support NE Plumbing Inc.’s online presence and internal site management. It provides:

- A public marketing website
- Service and home page content management
- A careers page and application form
- An authenticated admin dashboard
- Website metrics and message management
- Theme customization, including light/dark mode support

## Features

### Public Site
- Home page with editable content
- Services page
- Careers page
- Contact and message submission flow
- Responsive layout and navigation

### Admin Tools
- Protected admin dashboard
- Manage home page content
- Manage services
- Manage colors and theme settings
- View and manage website messages
- View website metrics
- User/admin authentication and authorization

### Platform / Tech
- Blazor
- ASP.NET Core
- C#
- Entity Framework Core
- ASP.NET Identity
- Azure hosting

## Project Structure

Some of the main areas of the app include:

- `Components/App.razor` — root HTML shell and early theme initialization
- `Components/Routes.razor` — router and authorization handling
- `Components/Layout/MainLayout.razor` — shared layout, navigation, and footer
- `Components/Pages/Home.razor` — public home page
- `Components/Pages/Services.razor` — services page
- `Components/Pages/Careers.razor` — careers page
- `Components/Pages/AdminDashboard.razor` — protected admin dashboard
- `Components/ManageHomePageContent.razor` — home page content editor
- `Components/ManageColors.razor` — theme/color settings editor
- `Components/ManageWebsiteMetrics.razor` — site analytics and metrics
- `Data/AppDbContext.cs` — database context and entity configuration
- `Migrations/` — Entity Framework Core migrations

## Getting Started

### Prerequisites
- .NET SDK
- SQL Server or the database configured in the application
- Visual Studio or Visual Studio Code

### Run Locally

1. Clone the repository:
   ```bash
   git clone https://github.com/BretPehrson/NEPlumbingInc.git
   cd NEPlumbingInc
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Run the app:
   ```bash
   dotnet run
   ```

4. Open the site in your browser using the URL shown in the terminal.

## Configuration

This project uses application settings for things like:

- database connection
- authentication / identity
- Azure deployment settings
- theme and content management data

Check the app configuration files and environment-specific settings before deploying.

## Deployment

The site is hosted on Azure. When deploying, make sure the production configuration includes:

- the correct database connection string
- identity/authentication settings
- any required Azure app settings
- updated build/publish configuration

## License

No license has been specified yet.
