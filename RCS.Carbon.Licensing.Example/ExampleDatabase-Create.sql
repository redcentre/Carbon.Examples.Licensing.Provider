--DROP TABLE [UserJob];
--DROP TABLE [UserCustomer];
--DROP TABLE [User];
--DROP TABLE [Job];
--DROP TABLE [Customer];
------------------------------------------------------------------------------------------------
CREATE TABLE [Customer]
(
	[Id] [int] NOT NULL,
	[Name] [nvarchar](32) NOT NULL,
	[DisplayName] [nvarchar](128) NULL,
	[StorageKey] [nvarchar](1024) NOT NULL,
	[Note] [nvarchar](1024) NULL,
	CONSTRAINT [PK_Customer_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO
CREATE UNIQUE INDEX [IX_Customer_Name] ON [Customer] ([Name]);
GO
INSERT INTO [Customer] VALUES(30000008,'client1rcs','Client-1 RCS', 'DefaultEndpointsProtocol=https;AccountName=zclient1rcs;AccountKey=SVWdI634A0GSqXoJt3mHjA3jMkKhI9FKRUVoVgcaPR1isxgAqfJk2aFemc7wZ60dcaoq/K9m1QMj+AStM3bk2A==;BlobEndpoint=https://zclient1rcs.blob.core.windows.net/;', NULL);
INSERT INTO [Customer] VALUES(30000011,'rcspublic','RCS Public', 'DefaultEndpointsProtocol=https;AccountName=zrcspublic;AccountKey=r2RIAuWzCnHXO+h8B3bZFGpsrUizaUZ+qYhviUsbXK0NH1sj0xXAu6CPnQ7mmlKLtgrx6abZFe16+AStrDyeZw==;BlobEndpoint=https://zrcspublic.blob.core.windows.net/;', NULL);
INSERT INTO [Customer] VALUES(30000022,'rcsruby','Ruby Samples', 'DefaultEndpointsProtocol=https;AccountName=zrcsruby;AccountKey=LKcyYfVJPTrnqWPjIH6km5W4/Nuv3AMxltgzJEnnfxD6Uo/jl+/AjW0EV7wPY4G52S8TiSh92zBb+AStnL5yaA==;BlobEndpoint=https://zrcsruby.blob.core.windows.net/;', NULL);
GO
------------------------------------------------------------------------------------------------
CREATE TABLE [Job]
(
	[Id] [int] NOT NULL,
	[CustomerId] [int] NOT NULL,
	[Name] [nvarchar](32) NOT NULL,
	[DisplayName] [nvarchar](128) NULL,
	[Note] [nvarchar](1024) NULL,
	[VarteeNames] [nvarchar](128) NOT NULL,
	CONSTRAINT [PK_Job_Id] PRIMARY KEY CLUSTERED ([Id] ASC),
	CONSTRAINT [FK_Job_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customer] ([Id])
);
GO
CREATE INDEX [IX_Job_Name] ON [Job] ([Name]);
GO
INSERT INTO [Job] VALUES(20000001, 30000008, '29997-google-test-carbon-project', 'Metrics Testing', NULL, 'VarTree');
INSERT INTO [Job] VALUES(20000002, 30000008, 'demo', 'Demo Testing', NULL, 'RubyLib,Test,TsapiTree,VarTree');
INSERT INTO [Job] VALUES(20000003, 30000011, 'aemo', 'Energy', NULL, 'VarTree');
INSERT INTO [Job] VALUES(20000004, 30000011, 'cdc-covid', 'CDC Covid', NULL, 'VarTree');
INSERT INTO [Job] VALUES(20000005, 30000011, 'firstfleet', 'First Fleet', NULL, 'VarTree');
INSERT INTO [Job] VALUES(20000006, 30000011, 'romeo-juliet', 'Romeo and Juliet', NULL, 'vartee');
INSERT INTO [Job] VALUES(20000007, 30000022, 'demo', 'Demo Ruby', NULL, 'TsapiTree,VarTree');
GO
--EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Comma joined list of plain variable tree blob names' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'Job', @level2type=N'COLUMN',@level2name=N'VartreeNames';
--GO
------------------------------------------------------------------------------------------------
CREATE TABLE [dbo].[User]
(
	[Id] [int] NOT NULL,
	[Name] [nvarchar](128) NOT NULL,
	[DisplayName] [nvarchar](64) NULL,
	[Email] [nvarchar](128) NULL,
	[Password] [nvarchar](32) NULL,
	[Note] [nvarchar](1024) NULL,
	CONSTRAINT [PK_User_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO
INSERT INTO [User] VALUES(10000013, 'john', 'John Citizen', 'john@mail.com', 'J0hn123', 'John is a normal user.');
INSERT INTO [User] VALUES(10000022, 'max', 'Max Power', 'max@powerhouse.com', 'Max1mum', 'Max can do anything.');
INSERT INTO [User] VALUES(10000335, 'guest', 'Guest User', NULL, 'guest', 'Guest user for evaluation and demos.');
GO
CREATE UNIQUE INDEX [IX_User_Name] ON [User] ([Name]);
GO
------------------------------------------------------------------------------------------------
CREATE TABLE [dbo].[UserCustomer]
(
	[UserId] [int] NOT NULL,
	[CustomerId] [int] NOT NULL,
	CONSTRAINT [PK_UserCustomer] PRIMARY KEY CLUSTERED ([UserId] ASC, [CustomerId] ASC),
	CONSTRAINT [FK_UserCustomer_UserId] FOREIGN KEY ([UserId]) REFERENCES [User] ([Id]),
	CONSTRAINT [FK_UserCustomer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customer] ([Id])
)
GO
INSERT INTO [UserCustomer] VALUES(10000013,30000011);
INSERT INTO [UserCustomer] VALUES(10000013,30000022);
INSERT INTO [UserCustomer] VALUES(10000022,30000008);
INSERT INTO [UserCustomer] VALUES(10000022,30000011);
INSERT INTO [UserCustomer] VALUES(10000022,30000022);
INSERT INTO [UserCustomer] VALUES(10000335,30000011);
INSERT INTO [UserCustomer] VALUES(10000335,30000022);
GO
------------------------------------------------------------------------------------------------
CREATE TABLE [dbo].[UserJob]
(
	[UserId] [int] NOT NULL,
	[JobId] [int] NOT NULL,
	CONSTRAINT [PK_UserJob] PRIMARY KEY CLUSTERED ([UserId] ASC, [JobId] ASC),
	CONSTRAINT [FK_UserJob_UserId] FOREIGN KEY ([UserId]) REFERENCES [User] ([Id]),
	CONSTRAINT [FK_UserJob_JobId] FOREIGN KEY ([JobId]) REFERENCES [Job] ([Id])
)
GO
INSERT INTO [UserJob] VALUES(10000335,20000002);
INSERT INTO [UserJob] VALUES(10000335,20000005);	-- This is a duplicate
GO
------------------------------------------------------------------------------------------------
--SELECT * FROM [Customer];
--SELECT * FROM [Job];
--SELECT * FROM [User];
--SELECT * FROM [UserCustomer];
--SELECT * FROM [UserJob];
------------------------------------------------------------------------------------------------
