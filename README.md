RDB-ClinixDocs: A Web-Based Clinic Patient Document Management System

This document describes the deployment architecture and the step-by-step process for deploying the RDB-ClinixDocs: A Web-Based Clinic Patient Document Management System to a cloud hosting environment.

The system is developed using ASP.NET Core MVC with Razor, C#, Entity Framework Core, and SQL Server Express.
The application is deployed to MonsterASP.NET cloud hosting, which supports ASP.NET applications and SQL Server databases.

1. Architecture Overview
- Component	Technology
- Frontend	ASP.NET Core Razor Views
- Backend	ASP.NET Core MVC (C#)
- Web Server	IIS (Managed by MonsterASP.NET)
- Database	SQL Server Express
- ORM	Entity Framework Core
- Version Control	GitHub

2. Preparing the Project Repository

Repository link:

https://github.com/Deextr/IT15_DMS_CLINIC_PATIENT.git

Push the latest project version using Git:

git add .
git commit -m "commit"
git push origin main

This ensures the project is properly version-controlled before deployment.

3. Database Configuration

The system uses SQL Server Express.

Database Name

DMS_DB

The database is managed using SQL Server Management Studio (SSMS).

Step 1: Configure Connection String
Open the file:
appsettings.json

Configure the database connection string:
"ConnectionStrings": {
  "DefaultConnection": "Server=SERVER_NAME;Database=DMS_DB;Trusted_Connection=True;MultipleActiveResultSets=True"
}

Step 2: Apply Database Migrations
Open Package Manager Console in Visual Studio and run:
Update-Database

This command creates and updates the required database tables.

4. Publishing the ASP.NET Core Application

Step 1: Publish the Project
In Visual Studio:
Right click the project
Select Publish
Choose Folder as the publish target
Click Publish

This will generate the compiled deployment files.

5. Uploading the Application to MonsterASP.NET
Log in to the MonsterASP.NET hosting control panel
Upload all published files using FTP (FileZilla)
Upload the files to the following directory:
/wwwroot

6. Configuring the Database on Hosting Server
Inside the MonsterASP.NET dashboard:
Create a SQL Server database
Set the database name:
DMS_DB

Create a database user
Obtain the database connection string
Update the connection string in:
appsettings.json

7. Running the Application
Open a web browser
Navigate to the hosting domain provided by MonsterASP.NET
The web application will load and connect to the configured database

8. Updating the Database
If changes are made to the database models, run the migration command again:
Update-Database

This will update the database schema to match the new models.

✅ Deployment Complete

The RDB-ClinixDocs system should now be successfully running on the cloud hosting server.
