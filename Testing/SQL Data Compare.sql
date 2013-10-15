USE [Source]
GO

DECLARE @Source varchar(100)
DECLARE @Destination varchar(100)
DECLARE @ParentTableName varchar(500)
DECLARE @TableName varchar(500)
DECLARE @PrimaryColumnList varchar(500)
DECLARE @ColumnList varchar(2000)
DECLARE @sql varchar(5000)

SET @Source = 'Source'
SET @Destination = 'Destination'

DECLARE iCursor CURSOR FOR
select t.name, i.name
from sys.internal_tables i
	inner join sys.tables t on i.parent_object_id = t.object_id
where i.internal_type_desc = 'CHANGE_TRACKING'
order by t.name
/*
SELECT 'MessageHistory' as 'name', 'MessageHistory' as 'name'
*/

OPEN iCursor
FETCH NEXT FROM iCursor INTO @ParentTableName, @TableName

WHILE @@FETCH_STATUS = 0
BEGIN
	BEGIN
		SET @PrimaryColumnList = null
		SELECT @PrimaryColumnList = COALESCE(@PrimaryColumnList + ', ', '') + '[' + COLUMN_NAME + ']'
			FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE table_name = @ParentTableName AND OBJECTPROPERTY(OBJECT_ID(constraint_name), 'IsPrimaryKey') = 1
		--print '@PrimaryColumnList: ' + @PrimaryColumnList

		SET @ColumnList = null
		-- SELECT COLUMN_NAME, * FROM information_schema.columns WHERE table_name = 'AuxiliaryDataHistory' ORDER BY ORDINAL_POSITION
		SELECT @ColumnList = COALESCE(@ColumnList + ', ', '') + '[' + COLUMN_NAME + ']'
			FROM information_schema.columns WHERE table_name = @ParentTableName AND DATA_TYPE NOT IN ('xml') ORDER BY ORDINAL_POSITION
		--print '@ColumnList: ' + @ColumnList

		set @sql = 
			'select MIN(TableSource) as TableSource, ' + @ColumnList +
			' from (
				select ''' + @Source + ':' + @ParentTableName + ''' AS TableSource, ' + @ColumnList +
				' from ' + @Source + '..' + @ParentTableName +
				' union all 
				select ''' + @Destination + ':' + @ParentTableName + ''' AS TableSource, ' + @ColumnList +
				' from ' + @Destination + '..' + @ParentTableName +
				' ) tmp
			 group by ' + @ColumnList +
			' HAVING COUNT(*) = 1
			order by ' + @PrimaryColumnList + ', TableSource'
		print '@sql: ' + @sql
		exec (@sql)

	END
	FETCH NEXT FROM iCursor INTO @ParentTableName, @TableName
END

CLOSE iCursor
DEALLOCATE iCursor
GO






