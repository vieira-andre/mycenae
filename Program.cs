﻿using Cassandra;
using CsvHelper;
using migration_pair.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace migration_pair
{
    class Program
    {
        private static readonly ConfigurableValues config = new ConfigurableValues();

        #region Source cluster & session
        private static Cluster sourceCluster;
        private static ISession sourceSession;
        #endregion

        #region Target cluster & session
        private static Cluster targetCluster;
        private static ISession targetSession;
        #endregion

        static void Main(string[] args)
        {
            Enum.TryParse(config.TaskToPerform, true, out TaskToPerform procedure);

            switch (procedure)
            {
                case TaskToPerform.Extract:
                    ExtractionPhase();
                    break;

                case TaskToPerform.Insert:
                    InsertionPhase();
                    break;

                case TaskToPerform.ExtractAndInsert:
                    ExtractionPhase();
                    InsertionPhase();
                    break;

                default:
                    Console.WriteLine("[Error] Config entry \"Task_To_Perfom\" is either unspecified or misspecified.");
                    _ = Console.ReadKey();
                    break;
            }
        }

        private static void BuildSourceClusterAndSession()
        {
            sourceCluster = Cluster.Builder().AddContactPoints(config.SourceEndPoints).Build();
            sourceSession = sourceCluster.Connect();
        }

        private static void BuildTargetClusterAndSession()
        {
            targetCluster = Cluster.Builder().AddContactPoints(config.TargetEndPoints).Build();
            targetSession = targetCluster.Connect();
        }

        private static void ExtractionPhase()
        {
            BuildSourceClusterAndSession();

            var ctable = new CTable(config.SourceTableName, config.SourceKeyspace);
            GetRows(ref ctable);

            DisposeSourceSessionAndCluster();

            var tableData = WriteResultsToObject(ctable);
            SaveResultsIntoFile(tableData, config.FilePath);
        }

        private static void InsertionPhase()
        {
            var tableData = ReadFromCsv(config.FilePath);

            BuildTargetClusterAndSession();

            List<CColumn> columns = GetColumnsForTable();
            InsertDataIntoTable(ref tableData, ref columns);

            DisposeTargetSessionAndCluster();
        }

        private static void GetRows(ref CTable ctable)
        {
            string cql = $"SELECT * FROM {ctable.Keyspace}.{ctable.Name}";
            var statement = new SimpleStatement(cql);
            RowSet results = sourceSession.Execute(statement);

            foreach (Row result in results)
            {
                CField[] row = new CField[result.Length];

                for (int i = 0; i < result.Length; i++)
                {
                    row[i] = results.Columns[i].Type.IsAssignableFrom(typeof(DateTimeOffset))
                        ? new CField(((DateTimeOffset)result[i]).ToUnixTimeMilliseconds(), results.Columns[i].Name, typeof(long))
                        : new CField(result[i], results.Columns[i].Name, results.Columns[i].Type);
                }

                ctable.Rows.Add(row);
            }
        }

        private static StringBuilder WriteResultsToObject(CTable ctable)
        {
            var tableData = new StringBuilder();

            foreach (CField[] row in ctable.Rows)
            {
                var rowToWrite = new List<string>(row.Length);

                foreach (CField cfield in row)
                {
                    string valueToWrite = Convert.ToString(cfield.Value);

                    if (cfield.DataType.Equals(typeof(string)) && !string.IsNullOrEmpty(valueToWrite))
                        valueToWrite = string.Format("\"{0}\"", valueToWrite.Replace("\"", "\"\""));

                    rowToWrite.Add(valueToWrite);
                }

                tableData.AppendLine(string.Join(",", rowToWrite));
            }

            return tableData;
        }

        private static void SaveResultsIntoFile(StringBuilder tableData, string filePath)
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, tableData.ToString());
        }

        private static List<string[]> ReadFromCsv(string filePath)
        {
            var tableData = new List<string[]>();

            using (TextReader reader = new StreamReader(filePath))
            using (var csvReader = new CsvReader(reader))
            {
                csvReader.Configuration.Delimiter = ",";
                csvReader.Configuration.HasHeaderRecord = false;
                csvReader.Configuration.MissingFieldFound = null;

                var records = csvReader.GetRecords<dynamic>();

                foreach (IDictionary<string, object> record in records)
                {
                    var row = new List<string>(record.Values.Count);

                    foreach (string value in record.Values)
                        row.Add(value);

                    tableData.Add(row.ToArray());
                }
            }

            return tableData;
        }

        private static List<CColumn> GetColumnsForTable()
        {
            var columns = new List<CColumn>();

            string cql = $"SELECT * FROM {config.TargetKeyspace}.{config.TargetTableName} LIMIT 1";
            var statement = new SimpleStatement(cql);
            RowSet results = targetSession.Execute(statement);

            foreach (CqlColumn column in results.Columns)
                columns.Add(new CColumn(column.Name, column.Type));

            return columns;
        }

        private static void InsertDataIntoTable(ref List<string[]> tableData, ref List<CColumn> columns)
        {
            string columnsAsString = string.Join(',', columns.GroupBy(c => c.Name).Select(c => c.Key));
            string valuesPlaceholders = string.Concat(Enumerable.Repeat("?,", columns.Count)).TrimEnd(',');

            string cql = $"INSERT INTO {config.TargetKeyspace}.{config.TargetTableName} ({columnsAsString}) VALUES ({valuesPlaceholders})";
            PreparedStatement pStatement = targetSession.Prepare(cql);

            foreach (string[] row in tableData)
            {
                var preparedRow = new List<dynamic>(row.Length);

                int i = 0;
                while (i < row.Length)
                {
                    preparedRow.Add(ConvertFieldValueToProperType(row[i], columns[i].DataType));
                    i++;
                }

                BoundStatement bStatement = pStatement.Bind(preparedRow.ToArray<dynamic>());
                _ = targetSession.Execute(bStatement);
            }
        }

        private static dynamic ConvertFieldValueToProperType(dynamic fieldValue, Type columnDataType)
        {
            if (columnDataType.Equals(typeof(long))) { return Convert.ToInt64(fieldValue); }
            if (columnDataType.Equals(typeof(int))) { return Convert.ToInt32(fieldValue); }
            if (columnDataType.Equals(typeof(short))) { return Convert.ToInt16(fieldValue); }
            if (columnDataType.Equals(typeof(DateTimeOffset))) { return Convert.ToInt64(fieldValue); }
            if (columnDataType.Equals(typeof(bool))) { return bool.Parse(fieldValue); }

            return fieldValue;
        }

        private static void DisposeSourceSessionAndCluster()
        {
            sourceSession.Dispose();
            sourceCluster.Dispose();
        }

        private static void DisposeTargetSessionAndCluster()
        {
            targetSession.Dispose();
            targetCluster.Dispose();
        }
    }
}