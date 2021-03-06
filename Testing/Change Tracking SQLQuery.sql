/*
http://msdn.microsoft.com/en-us/library/bb933994.aspx
http://msdn.microsoft.com/en-us/library/bb933875.aspx

http://visualstudiomagazine.com/articles/2012/06/01/database-synchronization-with-the-microsoft-sync-framework.aspx
http://techreadme.blogspot.com/2013/04/how-to-create-one-way-sync-application.html
http://mentormate.com/blog/database-synchronization-with-microsoft-sync-framework/
http://vivekcek.wordpress.com/2012/07/08/synchronizing-sql-server-databases-using-microsoft-sync-framework/

*/


/*
USE [master]
GO
--ALTER DATABASE [Source] SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON)
ALTER DATABASE [Source] SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 1 HOURS, AUTO_CLEANUP = ON)
--ALTER DATABASE [Source] SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 MINUTES, AUTO_CLEANUP = ON)
GO

USE [Source]
go
--	SETUP TEST TABLE
IF OBJECT_ID(N'UnPartitionTable') IS NOT NULL
	DROP TABLE UnPartitionTable
;
CREATE TABLE UnPartitionTable
	(ID INT NOT NULL,
	Date DATETIME NOT NULL,
	something VARCHAR(255) NOT NULL,
	Cost money ) on [primary]
go

ALTER TABLE dbo.UnPartitionTable ADD CONSTRAINT
	PK_UnPartitionTable PRIMARY KEY CLUSTERED 
	(
	ID
	) WITH( STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]

GO
ALTER TABLE dbo.UnPartitionTable SET (LOCK_ESCALATION = TABLE)
GO

ALTER TABLE UnPartitionTable
ENABLE CHANGE_TRACKING
WITH (TRACK_COLUMNS_UPDATED = ON)
GO
*/


/*
-- !NOTE! this will  delete the change tracking table
USE [Source]
go
ALTER TABLE UnPartitionTable
DISABLE CHANGE_TRACKING;
GO

ALTER DATABASE [Source]
SET CHANGE_TRACKING = OFF
GO
*/


/*
USE [Source]
go

--	SETUP DUMMY DATA
truncate table UnPartitionTable
go
declare @day int
declare @count int
set @day = 1
set @count = 0
while @day <= 90
begin
	while @count <= @day * 10
	begin
		insert into UnPartitionTable select @count, CONVERT(VARCHAR(10), GETDATE()-@day, 101), cast(@count as varchar), @day * 10000
		set @count=@count+1
	end
	--print @count
	--print getdate()-@day
	set @day=@day+1
end
go

--	UPDATE DUMMY DATA
declare @day int
declare @count int

set @day = 1
set @count = 0
while @day <= 90
begin
	while @count <= @day * 10
	begin
		UPDATE UnPartitionTable SET Cost = (@day * 10007) WHERE ID = @count
		set @count=@count+1
	end
	set @day=@day+1
end

declare @day int
declare @count int

SELECT @count = max(id) + 1 FROM UnPartitionTable
select @count
set @day = @count
begin
	--while @count <= @day * 1
	begin
		insert into UnPartitionTable select @count, CONVERT(VARCHAR(10), GETDATE()-@day, 101), cast(@count as varchar), @day * 10000
		set @count=@count+1
		delete from UnPartitionTable where ID = (@Count - 2)
	end
end
go

select count('n') from UnPartitionTable with (nolock)
*/


/*
-- view which tables have change tracking enabled
select * 
from sys.change_tracking_tables ctt
	inner join sys.tables t on ctt.object_id = t.object_id 

-- view the change table names
select t.name, i.name, * 
from sys.internal_tables i
	inner join sys.tables t on i.parent_object_id = t.object_id
where i.internal_type_desc = 'CHANGE_TRACKING'
	
-- view how much space is used for change tracking
EXEC sp_spaceused 'sys.syscommittab'

EXEC sp_spaceused 'sys.change_tracking_805577908'
*/


--select * from UnPartitionTable with (nolock)

DECLARE @ParentTableName varchar(500)
DECLARE @TableName varchar(500)

DECLARE iCursor CURSOR FOR
select t.name, i.name
from sys.internal_tables i
	inner join sys.tables t on i.parent_object_id = t.object_id
where i.internal_type_desc = 'CHANGE_TRACKING'
union select 'syscommittab' as 'parentname', 'syscommittab' as 'name'
order by t.name

OPEN iCursor
FETCH NEXT FROM iCursor INTO @ParentTableName, @TableName

WHILE @@FETCH_STATUS = 0
BEGIN
	BEGIN
		SELECT @ParentTableName as 'Parent Table Name'
		EXEC ('EXEC sp_spaceused ''sys.' + @TableName + '''')
	END
	FETCH NEXT FROM iCursor INTO @ParentTableName, @TableName
END

CLOSE iCursor
DEALLOCATE iCursor
GO


DECLARE @ParentTableName varchar(500)
DECLARE @TableName varchar(500)

DECLARE iCursor CURSOR FOR
select t.name, i.name
from sys.internal_tables i
	inner join sys.tables t on i.parent_object_id = t.object_id
where i.internal_type_desc = 'CHANGE_TRACKING'
order by t.name

OPEN iCursor
FETCH NEXT FROM iCursor INTO @ParentTableName, @TableName

WHILE @@FETCH_STATUS = 0
BEGIN
	BEGIN
		--EXEC ('EXEC sp_spaceused ''sys.' + @TableName + '''')
		EXEC ('SELECT ''C_' + @ParentTableName + ''', count(''x'') FROM [Source].[dbo].[' + @ParentTableName + '] with (nolock)')
		--EXEC ('SELECT ''CH_' + @ParentTableName + ''', count(''x'') FROM [Destination].[dbo].[' + @ParentTableName + '] with (nolock)')
	END
	FETCH NEXT FROM iCursor INTO @ParentTableName, @TableName
END

CLOSE iCursor
DEALLOCATE iCursor
GO


declare @min_synchronization_version bigint
SET @min_synchronization_version = CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('UnPartitionTable')) - 1
declare @synchronization_version bigint
SET @synchronization_version = CHANGE_TRACKING_CURRENT_VERSION()
select @min_synchronization_version as 'CHANGE_TRACKING_MIN_VALID_VERSION', @synchronization_version as 'CHANGE_TRACKING_CURRENT_VERSION'

SELECT
	CT.ID
    , t.*
	--, CT.*
    , CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_VERSION
FROM
    UnPartitionTable AS t with (nolock)
RIGHT OUTER JOIN
    --CHANGETABLE(CHANGES UnPartitionTable, null) AS CT
    CHANGETABLE(CHANGES UnPartitionTable, @min_synchronization_version) AS CT
ON
    t.ID = CT.ID
order by CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_VERSION

