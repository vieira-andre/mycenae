﻿using Cassandra;
using migration_pair.Helpers;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Logger = NLog.Logger;

namespace migration_pair.Models
{
    internal class Extraction : MigrationTask
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        internal override void Execute()
        {
            Logger.Info("Starting extraction phase...");

            try
            {
                var stopwatch = StopwatchManager.Start();

                BuildSourceClusterAndSession();

                RowSet rows = RetrieveRowsFromTable();

                ProcessRows(rows);

                stopwatch.StopAndLog();
            }
            catch (AggregateException aggEx)
            {
                foreach (Exception ex in aggEx.Flatten().InnerExceptions)
                    Logger.Error(ex);
            }
            catch (Exception ex)
            {
                Logger.Info(ex);
            }
            finally
            {
                DisposeSourceSessionAndCluster();
            }
        }

        private static void ProcessRows(RowSet rows)
        {
            Logger.Info("Processing rows...");

            _ = Directory.CreateDirectory(Path.GetDirectoryName(Config.FilePath));
            using var fileWriter = new StreamWriter(Config.FilePath);

            string columnNames = string.Join(',', rows.Columns.Select(c => c.Name));
            fileWriter.WriteLine(columnNames);

            foreach (Row row in rows)
            {
                CField[] rowFields = new CField[row.Length];

                for (int i = 0; i < row.Length; i++)
                {
                    rowFields[i] = rows.Columns[i].Type.IsAssignableFrom(typeof(DateTimeOffset))
                        ? new CField(((DateTimeOffset)row[i]).ToUnixTimeMilliseconds(), rows.Columns[i].Name, typeof(long))
                        : new CField(row[i], rows.Columns[i].Name, rows.Columns[i].Type);
                }

                string rowToWrite = PrepareRowToBeWritten(rowFields);

                fileWriter.WriteLine(rowToWrite);
            }
        }

        private static string PrepareRowToBeWritten(CField[] row)
        {
            var rowToWrite = new List<string>(row.Length);

            foreach (CField cfield in row)
            {
                string valueToWrite = Convert.ToString(cfield.Value);

                if (cfield.DataType == typeof(string) && !string.IsNullOrEmpty(valueToWrite))
                    valueToWrite = $"\"{valueToWrite.Replace("\"", "\"\"")}\"";

                rowToWrite.Add(valueToWrite);
            }

            return string.Join(',', rowToWrite);
        }
    }
}