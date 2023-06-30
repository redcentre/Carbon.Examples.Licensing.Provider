# Example Licence Provider

## Overview

As described the web article [Carbon Licensing][licarticle], the Carbon cross-tabulation engine supports the familiar security concepts of *authentication* which specifies which accounts can use the engine, and *authorization* which specifies which features of the engine are available to them.

The Carbon engine does not internally contain any logic to perform security functions, it delegates such functions to an external library called a *licensing provider*. [Red Centre Software][rcs] uses a proprietary provider when Carbon-based applications are hosted in their Azure subscription. Developers can create a custom provider for their own applications.

This project contains a class named `ExampleLicensingProvider` which uses a SQL Server database as the backing storage for account information and security permissions. The class implements a minimal fully working example of a custom licensing provider.

The following sections contain instructions on how to deploy and use the example provider. Because hosting environments and tool preferences are hard to predict accurately, the instructions are somewhat general, but should be sufficient guidance for an experienced developer to adjust to their needs.

---

## Create Database

The SQL Server database used by the example licensing provider can be hosted in a local server instance or in an Azure hosted server. The choice is dictated by where the calling application is running and which connection is most convenient and has acceptable performance.

### Local

To create a local database, the simplest option is to use SSMS (SQL Server Management Studio) to connect to a local server and create a new database using defult values in a suitable folder. Run the script in the file `ExampleDatabase-Create.sql` to create the database schema and inset sample rows.

### Azure

Use the Azure subscription web portal to create a SQL Server, if a suitable one doesn't exist. Create a SQL Server database named `CarbonLicensingExample` in the server.

Use SSMS to connect to the Azure database and run the script in `ExampleDatabase-Create.sql` to create the database schema and inset sample rows. The SSMS Server value will be in the format:

```
tcp:{SERVERNAME}.database.windows.net,1433
```

The SQL Server Authentication User Name and Password credentials are created when the Azure server is created in the portal. The connections strings are displayed in the Connection Strings blade for the database.

---

# Developer Notes

The following sections are technical notes about how the example provider project and files were created. The commands prefixed with ▶ are run in the "Open in Terminal" window for the example provider project.

Install packages:

```
Microsoft.EntityFrameworkCore.SqlServer 7.0.8
Microsoft.EntityFrameworkCore.Tools 7.0.8
```

The installed Tools might not be the latest. Use the following command to ensure the latest is installed:

```
▶ dotnet tool update --global dotnet-ef
```

Scaffold the classes from the database:

```
▶ cmd /c 'dotnet ef DbContext Scaffold "Server=tcp:{SERVERNAME}.database.windows.net,1433;Initial Catalog=CarbonLicensingExample;Persist Security Info=False;User ID=greg;Password={your_password};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;" Microsoft.EntityFrameworkCore.SqlServer --output-dir EFCore --data-annotations --context ExampleContext'
```

The generated classes will need adjusting to have all of the recommended [Data Annotation][annot] attributes. This has been done and the completed classes are in the EFCore folder of the example project.

[licarticle]: https://rcsapps.azurewebsites.net/doc/carbon/articles/licensing.htm
[rcs]: https://www.redcentresoftware.com/
[annot]: https://learn.microsoft.com/en-us/ef/ef6/modeling/code-first/data-annotations