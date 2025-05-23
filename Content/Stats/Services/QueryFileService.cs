﻿using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using IT.WebServices.Authentication;
using IT.WebServices.Content.Stats.Services.Data;
using IT.WebServices.Fragments.Content.Stats;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace IT.WebServices.Content.Stats.Services
{
    [Authorize()]
    public class QueryFileService : StatsQueryInterface.StatsQueryInterfaceBase
    {
        private readonly ILogger logger;
        private readonly IStatsContentPublicDataProvider cPubDb;
        private readonly IStatsContentPrivateDataProvider cPrvDb;
        private readonly IStatsUserPublicDataProvider uPubDb;
        private readonly IStatsUserPrivateDataProvider uPrvDb;

        public QueryFileService(ILogger<QueryFileService> logger, IStatsContentPublicDataProvider cPubDb, IStatsContentPrivateDataProvider cPrvDb, IStatsUserPublicDataProvider uPubDb, IStatsUserPrivateDataProvider uPrvDb)
        {
            this.logger = logger;
            this.cPubDb = cPubDb;
            this.cPrvDb = cPrvDb;
            this.uPubDb = uPubDb;
            this.uPrvDb = uPrvDb;
        }

        [AllowAnonymous]
        public override async Task<GetContentStatsResponse> GetContentStats(GetContentStatsRequest request, ServerCallContext context)
        {
            if (!Guid.TryParse(request.ContentID, out var contentId))
                return new();

            bool isLiked = false, isSaved = false, isViewed = false;
            float progress = 0;

            var userToken = ONUserHelper.ParseUser(context.GetHttpContext());
            if (userToken != null && userToken.IsLoggedIn)
            {
                var userRecord = await uPrvDb.GetById(userToken.Id);
                if (userRecord != null)
                {
                    isLiked = userRecord.Likes.Contains(request.ContentID);
                    isSaved = userRecord.Saves.Contains(request.ContentID);
                    isViewed = userRecord.Views.Contains(request.ContentID);

                    var rec = userRecord.ProgressRecords.FirstOrDefault(r => r.ContentID == request.ContentID);
                    if (rec != null)
                        progress = rec.Progress;
                }
            }

            return new()
            {
                Record = await cPubDb.GetById(contentId),
                LikedByUser = isLiked,
                SavedByUser = isSaved,
                ViewedByUser = isViewed,
                ProgressByUser = progress,
            };
        }

        [AllowAnonymous]
        public override async Task<GetOtherUserStatsResponse> GetOtherUserStats(GetOtherUserStatsRequest request, ServerCallContext context)
        {
            if (!Guid.TryParse(request.UserID, out var userId))
                return new();

            return new()
            {
                Record = await uPubDb.GetById(userId)
            };
        }

        public override async Task<GetOwnUserLikesResponse> GetOwnUserLikes(GetOwnUserLikesRequest request, ServerCallContext context)
        {
            var userToken = ONUserHelper.ParseUser(context.GetHttpContext());
            if (userToken == null || !userToken.IsLoggedIn)
                return new();

            var record = await uPrvDb.GetById(userToken.Id);

            var ret = new GetOwnUserLikesResponse();

            if (record != null)
                ret.LikedContentIDs.AddRange(record.Likes);

            return ret;
        }

        public override async Task<GetOwnUserProgressHistoryResponse> GetOwnUserProgressHistory(GetOwnUserProgressHistoryRequest request, ServerCallContext context)
        {
            var userToken = ONUserHelper.ParseUser(context.GetHttpContext());
            if (userToken == null || !userToken.IsLoggedIn)
                return new();

            var possiblyIDs = request.PossibleContentIDs.ToList();
            if (!possiblyIDs.Any())
                possiblyIDs = null;

            var record = await uPrvDb.GetById(userToken.Id);

            var ret = new GetOwnUserProgressHistoryResponse();

            if (record?.ProgressRecords != null)
                ret.Records.AddRange(record.ProgressRecords.OrderByDescending(r => r.UpdatedOnUTC));

            if (possiblyIDs != null)
            {
                var filtered = ret.Records.Where(r => possiblyIDs.Contains(r.ContentID)).ToArray();
                ret.Records.Clear();
                ret.Records.AddRange(filtered);
            }

            ret.PageTotalItems = (uint)ret.Records.Count();

            if (request.PageSize > 0)
            {
                ret.PageOffsetStart = request.PageOffset;

                var page = ret.Records.Skip((int)request.PageOffset).Take((int)request.PageSize).ToArray();
                ret.Records.Clear();
                ret.Records.AddRange(page);
            }

            ret.PageOffsetEnd = ret.PageOffsetStart + (uint)ret.Records.Count;

            return ret;
        }

        public override async Task<GetOwnUserSavesResponse> GetOwnUserSaves(GetOwnUserSavesRequest request, ServerCallContext context)
        {
            var userToken = ONUserHelper.ParseUser(context.GetHttpContext());
            if (userToken == null || !userToken.IsLoggedIn)
                return new();

            var record = await uPrvDb.GetById(userToken.Id);

            var ret = new GetOwnUserSavesResponse();

            if (record != null)
                ret.SavedContentIDs.AddRange(record.Saves);

            return ret;
        }

        public override async Task<GetOwnUserStatsResponse> GetOwnUserStats(GetOwnUserStatsRequest request, ServerCallContext context)
        {
            var userToken = ONUserHelper.ParseUser(context.GetHttpContext());
            if (userToken == null || !userToken.IsLoggedIn)
                return new();

            return new()
            {
                Record = await uPubDb.GetById(userToken.Id)
            };
        }
    }
}
