// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AppCenter.Ingestion.Models;
using Microsoft.AppCenter.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SQLite;

namespace Microsoft.AppCenter.Test
{
    [TestClass]
    public class StorageTest
    {
        private const string StorageTestChannelName = "storageTestChannelName";
        private Microsoft.AppCenter.Storage.Storage _storage = new Microsoft.AppCenter.Storage.Storage();

        [TestInitialize]
        public void InitializeStorageTest()
        {
            _storage.DeleteLogs(StorageTestChannelName);
        }

        [TestMethod]
        public void TestDatabaseIsInitialized()
        {
            var mockStorageAdapter = Mock.Of<IStorageAdapter>();
            var storage = new Microsoft.AppCenter.Storage.Storage(mockStorageAdapter);
            storage.WaitOperationsAsync(TimeSpan.FromSeconds(10)).Wait();

            // Verify database is initialized as a result of calling constructor.
            Mock.Get(mockStorageAdapter).Verify(adapter => adapter.CreateTableAsync<Microsoft.AppCenter.Storage.Storage.LogEntry>());
            Mock.Get(mockStorageAdapter).Verify(adapter => adapter.InitializeStorageAsync());
        }

        /// <summary>
        /// Verify that counting number of logs stored when there are no logs returns 0.
        /// </summary>
        [TestMethod]
        public void CountEmptyStorage()
        {
            var count = _storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            Assert.AreEqual(0, count);
        }

        /// <summary>
        /// Verify that after adding 'n' logs, counting logs returns 'n'.
        /// </summary>
        [TestMethod]
        public void CountNonemptyStorage()
        {
            var numLogsToAdd = 5;
            PutNLogs(numLogsToAdd);
            var count = _storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            Assert.AreEqual(numLogsToAdd, count);
        }

        /// <summary>
        /// Verify that storing a log and then retrieving it from storage does not alter the log.
        /// </summary>
        [TestMethod]
        public void PutOneLog()
        {
            var addedLog = TestLog.CreateTestLog();
            _storage.PutLog(StorageTestChannelName, addedLog);
            var retrievedLogs = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, 1, retrievedLogs).RunNotAsync();
            var retrievedLog = retrievedLogs[0];
            Assert.AreEqual(addedLog, retrievedLog);
        }

        /// <summary>
        /// Verify that deleting all logs for a given channel does so.
        /// </summary>
        [TestMethod]
        public void DeleteLogsNoBatchId()
        {
            PutNLogs(5);
            _storage.DeleteLogs(StorageTestChannelName);
            var count = _storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            Assert.AreEqual(0, count);
        }

        /// <summary>
        /// Verify that deleting a particular batch deletes exactly the number of logs for that batch.
        /// </summary>
        [TestMethod]
        public void DeleteLogsWithBatchId()
        {
            var numLogsToAdd = 5;
            var limit = 3;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            var batchId = _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            _storage.DeleteLogs(StorageTestChannelName, batchId);
            var numLogsRemaining = _storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            Assert.AreEqual(numLogsToAdd - retrievedLogs.Count, numLogsRemaining);
        }

        /// <summary>
        /// Verify that when the limit equals the number of logs for the given channel, all logs are returned.
        /// </summary>
        [TestMethod]
        public void GetLogsExactLimit()
        {
            var numLogsToAdd = 5;
            var limit = numLogsToAdd;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            CollectionAssert.AreEquivalent(addedLogs, retrievedLogs);
        }

        /// <summary>
        /// Verify that when the limit is lower than the number of logs for the given channel, all logs are returned.
        /// </summary>
        [TestMethod]
        public void GetLogsLowLimit()
        {
            var numLogsToAdd = 5;
            var limit = 3;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            Assert.AreEqual(limit, retrievedLogs.Count);
            CollectionAssert.IsSubsetOf(retrievedLogs, addedLogs);
        }

        /// <summary>
        /// Verify that when the limit exceeds the number of logs for the given channel, 'limit' logs are correctly returned.
        /// </summary>
        [TestMethod]
        public void GetLogsHighLimit()
        {
            var numLogsToAdd = 5;
            var limit = 7;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            CollectionAssert.AreEquivalent(retrievedLogs, addedLogs);
        }

        /// <summary>
        /// Verify that pending logs are not returned but other logs are.
        /// </summary>
        [TestMethod]
        public void GetLogsExcludesPendingLogsWithoutAffectingLimit()
        {
            var numLogsToAdd = 5;
            var limit = 5;

            // Add some logs and then retrieve them so they are marked as pending.
            PutNLogs(numLogsToAdd);
            _storage.GetLogsAsync(StorageTestChannelName, limit, new List<Log>()).RunNotAsync();
            
            // Add some new logs.
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();

            //  Retrieve logs and make sure all of the new ones are returned, but not the pending logs.
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            CollectionAssert.AreEquivalent(addedLogs, retrievedLogs);
        }

        /// <summary>
        /// Verify that when logs are retrieved, the batchId is not null.
        /// </summary>
        [TestMethod]
        public void GetLogsHasBatchId()
        {
            var numLogsToAdd = 5;
            var limit = numLogsToAdd;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            var batchId = _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            Assert.IsNotNull(batchId);
        }

        /// <summary>
        /// Verify that when no logs are retrieved, the batchId is null.
        /// </summary>
        [TestMethod]
        public void GetNoLogsHasNoBatchId()
        {
            var numLogsToAdd = 0;
            var limit = numLogsToAdd;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogs = new List<Log>();
            var batchId = _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogs).RunNotAsync();
            Assert.IsNull(batchId);
        }

        /// <summary>
        /// Verify that storage does not return same log more than once.
        /// </summary>
        [TestMethod]
        public void GetDuplicateLogs()
        {
            var numLogsToAdd = 5;
            var limit = numLogsToAdd;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogsFirstTry = new List<Log>();
            var retrievedLogsSecondTry = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogsFirstTry).RunNotAsync();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogsSecondTry).RunNotAsync();
            CollectionAssert.AreEquivalent(addedLogs, retrievedLogsFirstTry);
            Assert.AreEqual(0, retrievedLogsSecondTry.Count);
        }

        /// <summary>
        /// Verify that a channel that starts with the name of another channel does not cause problems.
        /// </summary>
        [TestMethod]
        public void GetLogsFromChannelWithSimilarNames()
        {
            var fakeChannelName = StorageTestChannelName.Substring(0, StorageTestChannelName.Length - 1);
            _storage.PutLog(StorageTestChannelName, TestLog.CreateTestLog());
            var retrievedLogs = new List<Log>();
            var batchId = _storage.GetLogsAsync(fakeChannelName, 1, retrievedLogs).RunNotAsync();
            Assert.IsNull(batchId);
        }

        /// <summary>
        /// Verify that storage returns log more than once if pending state is cleared.
        /// </summary>
        [TestMethod]
        public void ClearPendingState()
        {
            var numLogsToAdd = 5;
            var limit = numLogsToAdd;
            var addedLogs = PutNLogs(numLogsToAdd);
            var retrievedLogsFirstTry = new List<Log>();
            var retrievedLogsSecondTry = new List<Log>();
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogsFirstTry).RunNotAsync();
            _storage.ClearPendingLogState(StorageTestChannelName);
            _storage.GetLogsAsync(StorageTestChannelName, limit, retrievedLogsSecondTry).RunNotAsync();
            CollectionAssert.AreEquivalent(addedLogs, retrievedLogsFirstTry);
            CollectionAssert.AreEquivalent(addedLogs, retrievedLogsSecondTry);
        }

        /// <summary>
        /// Verify that an invalid log in the database, when retrieved, is deleted and no logs are returned.
        /// </summary>
        [TestMethod]
        public void FailToGetALog()
        {
            var invalidLogEntry = new Microsoft.AppCenter.Storage.Storage.LogEntry { Channel = StorageTestChannelName, Log = "good luck deserializing me!" };
            var connection = new SQLiteConnection("Microsoft.AppCenter.Storage");

            // Perform an arbitrary operation and wait on it to complete so that database is free when invalid log
            // is inserted.
            _storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            connection.Insert(invalidLogEntry);
            var logs = new List<Log>();
            var batchId = _storage.GetLogsAsync(StorageTestChannelName, 4, logs).RunNotAsync();
            var count = _storage.CountLogsAsync(StorageTestChannelName).RunNotAsync();
            Assert.IsNull(batchId);
            Assert.AreEqual(0, logs.Count);
            Assert.AreEqual(0, count);
        }

        #region Helper methods

        private List<TestLog> PutNLogs(int n)
        {
            var putLogTasks = new Task[n];
            var addedLogs = new List<TestLog>();
            for (var i = 0; i < n; ++i)
            {
                var testLog = TestLog.CreateTestLog();
                addedLogs.Add(testLog);
                putLogTasks[i] = _storage.PutLog(StorageTestChannelName, testLog);
            }
            return addedLogs;
        }

        #endregion
    }
}
