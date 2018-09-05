﻿// Copyright 2017 Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Firebase.Database;
using Firebase.Leaderboard;
using Firebase.Storage;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Hamster {
  public class TimeDataUtil {
    // Maximum rank records to download
    const int Max_Rank_Records = 5;

    // The root folder to store replay data.
    // The replay data are stored in {Storage_Replay_Root_Folder}/{MapType}/{MapID}/{RecordID}
    const string Storage_Replay_Root_Folder = "Replay/";

    // The postfix of the top rank record for each map
    const string Database_Ranks_Postfix = "Top/Ranks/";

    // The postfix of the top shared replay record for each map
    const string Database_Replays_Postfix = "Top/SharedReplays/";

    // Property name of time stored in the database.  Stored in milliseconds.
    const string Database_Property_Time = "time";

    // Property name of player display name stored in the database
    const string Database_Property_Name = "name";

    // Property name of storage path to replay record stored in the database
    const string Database_Property_ReplayPath = "replayPath";

    // Name of the property to determine whether the replay record is shared
    const string Database_Property_IsShared = "isShared";

    // Configuration of key, filename and paths to upload time record and replay data
    private struct UploadConfig {
      public string key;
      public string storagePath;
      public string dbRankPath;
      public string dbSharedReplayPath;
      public bool shareReplay;
    }

    // Uploads the time data to the database, and returns the current top time list.
    public static Task<StorageMetadata> UploadReplay(UserScore userScore, LevelMap map, ReplayData replay) {
      if (replay == null) {
        // Nothing to upload.
        return Task.FromResult<StorageMetadata>(null);
      }
      // Get a client-generated unique id based on timestamp and random number.
      string key = FirebaseDatabase.DefaultInstance.RootReference.Push().Key;

      UploadConfig config = new UploadConfig() {
        key = key,
        storagePath = replay != null ? Storage_Replay_Root_Folder + GetPath(map) + key : null,
        dbRankPath = GetDBRankPath(map) + key,
        dbSharedReplayPath = GetDBSharedReplayPath(map) + key,
        shareReplay = replay != null //TODO(chkuang): && GameOption.shareReplay
      };

      // Upload replay data first, update database, then retrieve top ranks
      return UploadReplayData(userScore, replay, config);
    }

    // Gets the path for the top ranks on the database, given the level's database path
    // and map id.
    public static string GetDBRankPath(LevelMap map) {
      return "Leaderboard/Map/" + GetPath(map) + Database_Ranks_Postfix;
    }

    // Gets the path, given the level's database path and map id.
    // This path is used for both database and storage.
    private static string GetPath(LevelMap map) {
      if (!string.IsNullOrEmpty(map.DatabasePath)) {
        return map.DatabasePath + "/";
      } else {
        return "OfflineMaps/" + map.mapId + "/";
      }
    }

    // Gets the path for the top shared replays on the database, given the level's database path
    // and map id.  Note that not everyone wants to share replay record
    private static string GetDBSharedReplayPath(LevelMap map) {
      return "Leaderboard/Map/" + GetPath(map) + Database_Replays_Postfix;
    }

    // Returns the top times, given the level's database path and map id.
    // Upload the replay data to Firebase Storage
    private static Task<StorageMetadata> UploadReplayData(
        UserScore userScore, ReplayData replay, UploadConfig config) {
      StorageReference storageRef =
        FirebaseStorage.DefaultInstance.GetReferenceFromUrl(
          CommonData.storageBucketUrl + "/" + config.storagePath);

      // Serializing replay data to byte array
      System.IO.MemoryStream stream = new System.IO.MemoryStream();
      replay.Serialize(stream);
      stream.Position = 0;
      byte[] serializedData = stream.ToArray();

      // Add database path and time to metadata for future usage
      MetadataChange newMetadata = new MetadataChange {
        CustomMetadata = new Dictionary<string, string> {
            {"DatabaseReplayPath", config.dbSharedReplayPath},
            {"DatabaseRankPath", config.dbRankPath},
            {"Time", userScore.Score.ToString()},
            {"Shared", config.shareReplay.ToString()},
          }
      };

      return storageRef.PutBytesAsync(serializedData, newMetadata);
    }

    // Utility function to get the top shared replay record storage path from the database
    public static Task<string> GetBestSharedReplayPathAsync(LevelMap map) {
      DatabaseReference reference =
        FirebaseDatabase.DefaultInstance.GetReference(GetDBSharedReplayPath(map));

      return reference.OrderByChild(Database_Property_Time).LimitToFirst(1).GetValueAsync()
        .ContinueWith((task) => {
          Dictionary<string, object> replays = task.Result.Value as Dictionary<string, object>;

          if (replays == null || replays.Count == 0) {
            return null;
          }

          // Return the first record in the dictionary
          Dictionary<string, object>.Enumerator e = replays.GetEnumerator();
          e.MoveNext();
          Dictionary<string, object> record = e.Current.Value as Dictionary<string, object>;
          if (record != null && record[Database_Property_ReplayPath] != null) {
            return record[Database_Property_ReplayPath] as string;
          }

          return null;
        });
    }
  }
}
