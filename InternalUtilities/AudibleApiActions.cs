﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AudibleApi;
using AudibleApi.Common;
using Dinah.Core;
using Polly;
using Polly.Retry;

namespace InternalUtilities
{
	public static class AudibleApiActions
	{
		/// <summary>USE THIS from within Libation. It wraps the call with correct JSONPath</summary>
		public static Task<Api> GetApiAsync(string username, string localeName, ILoginCallback loginCallback = null)
		{
			Serilog.Log.Logger.Information("GetApiAsync. {@DebugInfo}", new
			{
				Username = username.ToMask(),
				LocaleName = localeName,
			});
			return EzApiCreator.GetApiAsync(
				Localization.Get(localeName),
				AudibleApiStorage.AccountsSettingsFile,
				AudibleApiStorage.GetIdentityTokensJsonPath(username, localeName),
				loginCallback);
		}

		/// <summary>USE THIS from within Libation. It wraps the call with correct JSONPath</summary>
		public static Task<Api> GetApiAsync(ILoginCallback loginCallback, Account account)
		{
			Serilog.Log.Logger.Information("GetApiAsync. {@DebugInfo}", new
			{
				Account = account?.MaskedLogEntry ?? "[null]",
				LocaleName = account?.Locale?.Name
			});
			return EzApiCreator.GetApiAsync(
				account.Locale,
				AudibleApiStorage.AccountsSettingsFile,
				account.GetIdentityTokensJsonPath(),
				loginCallback);
		}

		private static AsyncRetryPolicy policy { get; }
			= Policy.Handle<Exception>()
			// 2 retries == 3 total
			.RetryAsync(2);

		public static Task<List<Item>> GetLibraryValidatedAsync(Api api, LibraryOptions.ResponseGroupOptions responseGroups = LibraryOptions.ResponseGroupOptions.ALL_OPTIONS)
		{
			// bug on audible's side. the 1st time after a long absence, a query to get library will return without titles or authors. a subsequent identical query will be successful. this is true whether or tokens are refreshed
			// worse, this 1st dummy call doesn't seem to help:
			//    var page = await api.GetLibraryAsync(new AudibleApi.LibraryOptions { NumberOfResultPerPage = 1, PageNumber = 1, PurchasedAfter = DateTime.Now.AddYears(-20), ResponseGroups = AudibleApi.LibraryOptions.ResponseGroupOptions.ALL_OPTIONS });
			// i don't want to incur the cost of making a full dummy call every time because it fails sometimes
			return policy.ExecuteAsync(() => getItemsAsync(api, responseGroups));
		}

		private static async Task<List<Item>> getItemsAsync(Api api, LibraryOptions.ResponseGroupOptions responseGroups)
		{
			var items = await api.GetAllLibraryItemsAsync(responseGroups);

			// remove episode parents
			items.RemoveAll(i => i.IsEpisodes);

			#region // episode handling. doesn't quite work
			//				// add individual/children episodes
			//				var childIds = items
			//					.Where(i => i.Episodes)
			//					.SelectMany(ep => ep.Relationships)
			//					.Where(r => r.RelationshipToProduct == AudibleApi.Common.RelationshipToProduct.Child && r.RelationshipType == AudibleApi.Common.RelationshipType.Episode)
			//					.Select(c => c.Asin)
			//					.ToList();
			//				foreach (var childId in childIds)
			//				{
			//					var bookResult = await api.GetLibraryBookAsync(childId, AudibleApi.LibraryOptions.ResponseGroupOptions.ALL_OPTIONS);
			//					var bookItem = AudibleApi.Common.LibraryDtoV10.FromJson(bookResult.ToString()).Item;
			//					items.Add(bookItem);
			//				}
			#endregion

			var validators = new List<IValidator>();
			validators.AddRange(getValidators());
			foreach (var v in validators)
			{
				var exceptions = v.Validate(items);
				if (exceptions != null && exceptions.Any())
					throw new AggregateException(exceptions);
			}

			return items;
		}

		private static List<IValidator> getValidators()
		{
			var type = typeof(IValidator);
			var types = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(s => s.GetTypes())
				.Where(p => type.IsAssignableFrom(p) && !p.IsInterface);

			return types.Select(t => Activator.CreateInstance(t) as IValidator).ToList();
		}
	}
}
