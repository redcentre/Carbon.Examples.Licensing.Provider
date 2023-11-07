# Licensing Overview

The Carbon cross-tabulation engine depends upon a licensing system to define rules for authenticating access to data.

[Red Centre Software][rcs] uses a proprietary licensing system when Carbon accesses jobs in Azure storage accounts hosted by Red Centre Software. A company that wishes to sever ties with Red Centre Software hosting and use Carbon to access jobs in their own Azure subscription must provide their own licensing system to the Carbon engine. A .NET class that acts as a licensing provider must implement the `ILicensingProvider` interface. The full interface contains about 30 members, but a minimal working provider only needs to implement the following:

```
public interface ILicensingProvider : IDisposable
{
  string Name { get; }
  string Description { get; }
  Task<LicenceFull> LoginId(string userId, string password, bool skipCache = false);
  Task<LicenceFull> LoginName(string userName, string password);
  Task<LicenceFull> GetFreeLicence(string clientIdentifier = null, bool skipCache = false);
  Task<int> ReturnId(string userId);
  Task<int> LogoutId(string userId);
}
```

Other unused interface members can be implemented like this:

```
  public Task<NavData> GetNavigationData() => throw new NotImplementedException();
  // etc
```

The other interface members are only needed if the provider wishes to be a general-purpose licensing database management plug-in to the Midas Licensing Management Tool, which will be described in an upcoming Wiki page.

---

Carbon recognises the following fundamental definitions of parts of a licensing provider:

**User Name**

> A traditional sign-in (or login) name credential. The value is normally a readable string such as a person's full or partial name. The value may also be a unique readable value such as an email address.

**User Id**

> Many licensing providers can use a database identifier (or key) as a credential. The value may often be a string of computer generated characters that are not particularly memorable. Individual licensing systems may choose to support Name or Id credentials, or both, and choose the behaviour of each one.

**Password**

> If a licensing provider requires secure authentication then it can support assigning passwords to user Name or Id credentials. Each licensing provider will implement its own internal way of storing credentials.

**Customer**

> Carbon equates an Azure storage account to a customer. The licensing provider must define which storage accounts represent customers.

**Job**

> Carbon equates an Azure container to a job. The licensing provider must define which containers represent jobs.

**Caching**

> A licensing provider may wish to support caching of licensing responses to improve performance. How this could be done is an internal detail for each licensing provider. Some of the licensing interface methods take a skipCache flag which can diasble caching if it is active by default.

---

# Example Licence Provider

## Overview

As described in the previous section, the Carbon cross-tabulation engine supports the familiar security concepts of *authentication* which specifies which accounts can use the engine, and *authorization* which specifies which features of the engine are available to them.

The Carbon engine does not internally contain any logic to perform security functions, it delegates such functions to an external library called a *licensing provider*. [Red Centre Software][rcs] uses a proprietary provider when Carbon-based applications are hosted in their Azure subscription. Developers can create a custom provider for their own applications.

This project contains a class named `ExampleLicensingProvider` which uses a SQL Server database as the backing storage for account information and security permissions. The class implements a minimal fully working example of a custom licensing provider.

The following sections contain instructions on how to deploy and use the example provider. Because hosting environments and tool preferences are hard to predict accurately, the instructions are somewhat general, but should be sufficient guidance for an experienced developer to adjust to their needs.

## Create Database

The SQL Server database used by the example licensing provider can be hosted in a local server instance or in an Azure hosted server. The choice is dictated by where the calling application is running and which connection is most convenient and has acceptable performance.

### Local

To create a local database, the simplest option is to use SSMS (SQL Server Management Studio) to connect to a local server and create a new database using defult values and a suitable folder. Run the script in the file `ExampleDatabase-Create.sql` to create the database schema and inset sample rows.

### Azure

Use the Azure subscription web portal to create a SQL Server, if a suitable one doesn't exist. Create a SQL Server database named `CarbonLicensingExample` in the server.

Use SSMS to connect to the Azure database and run the script in `ExampleDatabase-Create.sql` to create the database schema and insert sample rows. The SSMS Server value will be in the format:

```
tcp:{SERVERNAME}.database.windows.net,1433
```

The SQL Server Authentication User Name and Password credentials are created when the Azure server is created in the portal. The connections strings are displayed in the Connection Strings blade for the database.

---

## Developer Notes

The following sections are technical notes about how the example provider project and files were created. The commands prefixed with ▶ are run in the "Open in Terminal" window for the example provider project.

Install packages:

```
Microsoft.EntityFrameworkCore.SqlServer 7.0.9
Microsoft.EntityFrameworkCore.Tools 7.0.9
```

The installed Tools might not be the latest. Use the following command to ensure the latest is installed:

```
▶ dotnet tool update --global dotnet-ef
```

Scaffold the classes from the database:

```
▶ dotnet ef DbContext Scaffold "Server=tcp:{SERVERNAME}.database.windows.net,1433;Database={DATABASE};Persist Security Info=False;User ID={USER};Password={PASSWORD};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30;" Microsoft.EntityFrameworkCore.SqlServer --output-dir EFCore --data-annotations --context ExampleContext --no-onconfiguring --force
```

The generated classes will need adjusting to have all of the recommended [Data Annotation][annot] attributes. This has been done in the example project and the completed classes are in the EFCore folder of the example project.

[rcs]: https://www.redcentresoftware.com/
[annot]: https://learn.microsoft.com/en-us/ef/ef6/modeling/code-first/data-annotations
