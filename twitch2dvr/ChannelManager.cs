﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Swan.Logging;
using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using TwitchLib.Api.Helix.Models.Users.GetUserFollows;
using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace twitch2dvr
{
    /// <summary>
    /// A class that can provide Twitch channel information
    /// </summary>
    public class ChannelManager
    {
        /// <summary>
        /// Populates and returns channel information. Set <paramref name="updateChannelMode"/> to determine what kind of update is performed.
        /// Note that it is a flags enum, so pass all that apply.
        /// </summary>
        public static async Task<List<Channel>> UpdateChannels(UpdateChannelMode updateChannelMode)
        {
            if (updateChannelMode.HasFlag(UpdateChannelMode.Retrieve))
            {
                _channels = await RetrieveChannels();
            }

            if (updateChannelMode.HasFlag(UpdateChannelMode.Status))
            {
                // We were asked to update statuses. Let's make sure we actually have a list of channels to update
                if (_channels.Any() == false)
                {
                    "Trying to update channels' statuses, but no channels were found.".Log(nameof(UpdateChannels), LogLevel.Warning);
                }

                // Now update the status
                foreach (Channel channel in _channels)
                {
                    await UpdateLiveStatus(channel);
                }
            }

            return _channels;
        }

        /// <summary>
        /// Updates the live status (whether the streamer is live and other related data) for the given <paramref name="channel"/>.
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>
        public static async Task UpdateLiveStatus(Channel channel)
        {
            // See if the user is streaming
            Stream stream = (await TwitchApi.Helix.Streams.GetStreamsAsync(userIds: new List<string> {channel.ChannelNumber})).Streams.FirstOrDefault();

            channel.IsLive = stream is { };
            channel.LiveStreamId = stream?.Id;
            channel.UserName = stream?.UserLogin;
            channel.LiveGameName = stream?.GameName;
            channel.LiveStreamTitle = stream?.Title;
            channel.LiveStreamStartedDateTime = stream?.StartedAt;

            // If the user is streaming, get the game art
            if (stream is { })
            {
                var game = (await TwitchApi.Helix.Games.GetGamesAsync(new List<string> {stream.GameId})).Games.FirstOrDefault();
                channel.LiveGameArtUrl = game?.BoxArtUrl.Replace("{width}", "272").Replace("{height}", "380");
            }
        }

        private static async Task<List<Channel>> RetrieveChannels()
        {
            List<Channel> channels = new List<Channel>();

            // Get the Twitch user
            User twitchUser = (await TwitchApi.Helix.Users.GetUsersAsync(logins: new List<string> { Config.TwitchUsername })).Users.FirstOrDefault();

            if (twitchUser is null)
            {
                $"Unable to find Twitch user {Config.TwitchUsername}".Log(nameof(UpdateChannels), LogLevel.Error);
                Environment.Exit(1);
            }

            string page = string.Empty;
            List<Follow> userFollows = new List<Follow>();

            // Get the users that the user follows. Have to use pagination with this call.
            do
            {
                GetUsersFollowsResponse response = (await TwitchApi.Helix.Users.GetUsersFollowsAsync(fromId: twitchUser.Id, after: page));
                userFollows.AddRange(response.Follows);
                page = response.Pagination.Cursor;
            } while (string.IsNullOrEmpty(page) == false);
            

            $"Found that user {twitchUser.DisplayName} follows {userFollows.Count} channels: {string.Join(", ", userFollows.Select(x => x.ToUserName))}".Log(nameof(RetrieveChannels), LogLevel.Info);

            // Translate those follows into users
            User[] followedUsers = (await TwitchApi.Helix.Users.GetUsersAsync(ids: userFollows.Select(x => x.ToUserId).ToList())).Users;

            $"Translated {userFollows.Count} follows into {followedUsers.Length} users: {string.Join(", ", followedUsers.Select(u => u.DisplayName).ToArray())}".Log(nameof(RetrieveChannels), LogLevel.Info);

            // Translate those users into Channels
            foreach (User followedUser in followedUsers)
            {
                Channel channel = new Channel
                {
                    DisplayName = followedUser.DisplayName,
                    ChannelNumber = followedUser.Id,
                    ProfileImageUrl = followedUser.ProfileImageUrl
                };

                channels.Add(channel);
            }

            return channels;
        }

        private static List<Channel> _channels = new List<Channel>();

        private static readonly TwitchAPI TwitchApi = new TwitchAPI(settings: new ApiSettings
        {
            ClientId = Config.ClientId,
            AccessToken = Config.AccessToken
        });
    }

    /// <summary>
    /// Describes the type of update that should be performed when calling <see cref="ChannelManager.UpdateChannels"/>.
    /// </summary>
    [Flags]
    public enum UpdateChannelMode
    {
        /// <summary>
        /// Do not perform any updates, simply return the prepopulated channels
        /// </summary>
        None,

        /// <summary>
        /// Retrieves all followed channels for a given user
        /// </summary>
        Retrieve,

        /// <summary>
        /// Updates the status (i.e., whether they are live streaming) for each channel
        /// </summary>
        Status
    }
}
