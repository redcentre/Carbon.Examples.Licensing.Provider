using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RCS.Carbon.Licensing.Shared;
using RCS.Carbon.Licensing.Shared.Entities;

namespace RCS.Carbon.Licensing.Example.MSTests;

[TestClass]
public class ExampleProviderTests : TestBase
{
	const string GuestId = "10000335";
	const string TestUserName = "gfkeogh@gmail.com";
	const string TestUserPass = "qwe_123";
	const string GuestPass = "guest";
	const string Client1CustName = "client1rcs";
	const string Client1DemoName = "demo";
	readonly static JsonSerializerOptions JOpts = new() { WriteIndented = true };

	[TestMethod]
	public async Task T040_Realms()
	{
		var prov = MakeProvider();
		LicenceFull? licfull = await prov.AuthenticateName(TestUserName, TestUserPass);
		Assert.IsTrue(licfull.Realms.Length == 0);

		licfull = await prov.AuthenticateName("demo1@testusers.com", "demo123");
		//PrintJson(licfull);
		Assert.IsTrue(licfull.Realms.Length == 1);

		var users = await prov.ListUsers(licfull.Realms[0].Id);
		Info($"Realm {licfull.Realms[0].Name} users count -> {users.Length}");

		users = await prov.ListUsers();
		Info($"All users count -> {users.Length}");
	}

	[TestMethod]
	public async Task T100_Authenticate()
	{
		var prov = MakeProvider();
		LicenceFull? licfull = await prov.AuthenticateName(TestUserName, TestUserPass);
		Info($"LoginName -> {licfull.Id} | {licfull.Name}");
		foreach (var cust in licfull.Customers)
		{
			Info($"|  CUST {cust.Id} | {cust.Name} | {cust.DisplayName}");
			foreach (var job in cust.Jobs)
			{
				string? vtrs = job.VartreeNames == null ? null : string.Format("[{0}]", string.Join(',', job.VartreeNames));
				string? reals = job.RealCloudVartreeNames == null ? null : string.Format("[{0}]", string.Join(',', job.RealCloudVartreeNames));
				Info($"|  |  JOB {job.Id} | {job.Name} | {job.DisplayName} • {vtrs} • {reals}");
			}
		}
	}

	[TestMethod]
	public async Task T200_UpdateAccount()
	{
		var prov = MakeProvider();
		LicenceFull? licfull = await prov.AuthenticateName(TestUserName, TestUserPass);
		Info($"LoginName -> {licfull.Id} | {licfull.Name}");
		string comment = $"Updated on {DateTime.Now}";
		int ucount = await prov.UpdateAccount(licfull.Id, "GregKeogh", comment, "greg@orthogonal.com.au");
		Info($"Update count -> {ucount}");
		var user = await prov.ReadUser(licfull.Id);
		Assert.AreEqual("GregKeogh", user.Name);
		Assert.AreEqual(comment, user.Comment);
		Assert.AreEqual("greg@orthogonal.com.au", user.Email);
	}

	[TestMethod]
	public async Task T400_Connect_Story()
	{
		var prov = MakeProvider();
		LicenceFull? licfull = await prov.AuthenticateName(TestUserName, TestUserPass);
		Info($"LoginName -> {licfull.Id} | {licfull.Name}");

		const string Realm1Name = "TempTestRealm1";
		const string User1Name = "temp1@testing.com.au";
		const string User2Name = "temp2@testing.com.au";

		async Task<string> EnsureRealm(string realmName)
		{
			var realms = await prov.ReadRealmsByName(realmName);
			Assert.IsTrue(realms.Length <= 1);
			if (realms.Length == 0)
			{
				var realm = new Realm() { Name = realmName };
				var realm2 = await prov.UpdateRealm(realm);
				Info($"Created realm {realm2.Id} {realm2.Name}");
				return realm2.Id;
			}
			else
			{
				Info($"Realm {realms[0].Id} {realms[0].Name} already exists");
				return realms[0].Id;
			}
		}
		string rid1 = await EnsureRealm(Realm1Name);

		async Task<string> EnsureUser(string userName)
		{
			var users = await prov.ReadUsersByName(userName);
			Assert.IsTrue(users.Length <= 1);
			if (users.Length == 0)
			{
				var user = new User() { Name = userName, Roles = new string[] { "Analyst", "Silver" }, Comment = "TESTING ONLY", Psw = "test123" };
				var user2 = await prov.UpdateUser(user);
				Info($"Created user {user2.Id} {user2.Name}");
				return user2.Id;
			}
			else
			{
				Info($"User {users[0].Id} {users[0].Name} already exists");
				return users[0].Id;
			}
		}
		string uid1 = await EnsureUser(User1Name);
		string uid2 = await EnsureUser(User2Name);

		await prov.ConnectUserChildRealms(uid1, new string[] { rid1 });
		//await prov.ReplaceUserChildRealms(uid1, new string[] { rid1 });
		//await prov.ConnectRealmChildUsers(rid1, new string[] { uid1 });
	}

	ILicensingProvider MakeProvider()
	{
		IConfiguration config = new ConfigurationBuilder()
			.AddJsonFile("appsettings.json")
			.AddUserSecrets("9c9dd5cd-7323-46c4-aca6-b9289c171e54")
			.Build();
		string? lickey = config["CarbonApi:LicenceKey"];
		string? connect = config["CarbonApi:AdoConnect"];
		Assert.IsNotNull(lickey, "A licence key must be defined in configuration. Use the settings file, user secrets (in development) or another configuration source to provide the value.");
		Assert.IsNotNull(connect, "An ADO connection string to the SQL Server database must be defined in configuration. Use the settings file, user secrets (in development) or another configuration source to provide the value.");
		Info(connect);
		var prov = new ExampleLicensingProvider(lickey, connect);
		Info(prov.Description);
		return prov;
	}
}