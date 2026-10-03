/*
    User Management - database setup
    Run once against your SQL Server instance (SSMS, Azure Data Studio, or sqlcmd):
        sqlcmd -S localhost -E -i CreateDatabase.sql
    The script is re-runnable: it only creates objects that do not exist yet.
*/

IF DB_ID(N'UserManagement') IS NULL
    CREATE DATABASE UserManagement;
GO

USE UserManagement;
GO

/* ---------- States lookup (50 states + DC) ---------- */
IF OBJECT_ID(N'dbo.States', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.States
    (
        StateCode CHAR(2)      NOT NULL CONSTRAINT PK_States PRIMARY KEY,
        StateName NVARCHAR(50) NOT NULL
    );
END
GO

MERGE dbo.States AS target
USING (VALUES
    ('AL','Alabama'),('AK','Alaska'),('AZ','Arizona'),('AR','Arkansas'),('CA','California'),
    ('CO','Colorado'),('CT','Connecticut'),('DE','Delaware'),('DC','District of Columbia'),('FL','Florida'),
    ('GA','Georgia'),('HI','Hawaii'),('ID','Idaho'),('IL','Illinois'),('IN','Indiana'),
    ('IA','Iowa'),('KS','Kansas'),('KY','Kentucky'),('LA','Louisiana'),('ME','Maine'),
    ('MD','Maryland'),('MA','Massachusetts'),('MI','Michigan'),('MN','Minnesota'),('MS','Mississippi'),
    ('MO','Missouri'),('MT','Montana'),('NE','Nebraska'),('NV','Nevada'),('NH','New Hampshire'),
    ('NJ','New Jersey'),('NM','New Mexico'),('NY','New York'),('NC','North Carolina'),('ND','North Dakota'),
    ('OH','Ohio'),('OK','Oklahoma'),('OR','Oregon'),('PA','Pennsylvania'),('RI','Rhode Island'),
    ('SC','South Carolina'),('SD','South Dakota'),('TN','Tennessee'),('TX','Texas'),('UT','Utah'),
    ('VT','Vermont'),('VA','Virginia'),('WA','Washington'),('WV','West Virginia'),('WI','Wisconsin'),
    ('WY','Wyoming')
) AS source (StateCode, StateName)
ON target.StateCode = source.StateCode
WHEN NOT MATCHED THEN INSERT (StateCode, StateName) VALUES (source.StateCode, source.StateName);
GO

/* ---------- Users ---------- */
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        UserId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        FirstName    NVARCHAR(50)  NOT NULL,
        LastName     NVARCHAR(50)  NOT NULL,
        DateOfBirth  DATE          NOT NULL,
        Phone        CHAR(10)      NOT NULL,   -- digits only, formatted in the UI
        AddressLine1 NVARCHAR(100) NOT NULL,
        AddressLine2 NVARCHAR(100) NULL,
        City         NVARCHAR(50)  NOT NULL,
        StateCode    CHAR(2)       NOT NULL CONSTRAINT FK_Users_States REFERENCES dbo.States (StateCode),
        Zip5         CHAR(5)       NOT NULL,
        Zip4         CHAR(4)       NULL,
        CreatedAt    DATETIME2(0)  NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt    DATETIME2(0)  NULL,

        CONSTRAINT CK_Users_Phone CHECK (Phone NOT LIKE '%[^0-9]%' AND LEN(Phone) = 10),
        CONSTRAINT CK_Users_Zip5  CHECK (Zip5 LIKE '[0-9][0-9][0-9][0-9][0-9]'),
        CONSTRAINT CK_Users_Zip4  CHECK (Zip4 IS NULL OR Zip4 LIKE '[0-9][0-9][0-9][0-9]'),
        CONSTRAINT CK_Users_DOB   CHECK (DateOfBirth >= '1900-01-01')
    );

    CREATE INDEX IX_Users_Name ON dbo.Users (LastName, FirstName);
END
GO

/* ---------- Optional sample data (only when the table is empty) ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Users)
BEGIN
    INSERT INTO dbo.Users (FirstName, LastName, DateOfBirth, Phone, AddressLine1, AddressLine2, City, StateCode, Zip5, Zip4)
    VALUES
        (N'John',  N'Smith',   '1985-04-12', '2125550143', N'350 5th Ave',       N'Suite 2100', N'New York',    'NY', '10118', '0110'),
        (N'Maria', N'Garcia',  '1992-11-03', '3105550178', N'1200 Ocean Blvd',   NULL,          N'Santa Monica','CA', '90401', NULL),
        (N'David', N'Johnson', '1978-07-25', '5125550199', N'800 Congress Ave',  N'Apt 4B',     N'Austin',      'TX', '78701', '2436'),
        (N'Emily', N'Brown',   '2000-01-30', '3055550120', N'45 Biscayne Blvd',  NULL,          N'Miami',       'FL', '33132', NULL),
        (N'Robert',N'Lee',     '1965-09-14', '3125550111', N'233 S Wacker Dr',   N'Floor 12',   N'Chicago',     'IL', '60606', '6306');
END
GO
