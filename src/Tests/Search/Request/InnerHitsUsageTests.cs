﻿using System;
using System.Collections.Generic;
using System.Linq;
using Bogus;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using Tests.Framework;
using Tests.Framework.Integration;
using Tests.Framework.MockData;
using Xunit;
using static Nest.Static;
using ClientCall = Tests.Framework.Integration.ClientCall;

namespace Tests.Search.Request
{
	public interface IRoyal
	{
		string Name { get; set; }
	}

	[ElasticsearchType(IdProperty = "Name")]
	public abstract class RoyalBase<TRoyal> : IRoyal
		where TRoyal : class, IRoyal
	{
		public string Name { get; set; }
		public static Faker<TRoyal> Generator { get; } =
			new Faker<TRoyal>()
				.RuleFor(p => p.Name, f => f.Person.Company.Name);
	}

	public class King : RoyalBase<King>
	{
		public List<King> Foes { get; set; } 
		public new static Faker<King> Generator { get; } =
			RoyalBase<King>.Generator
				.RuleFor(p => p.Foes, f=>RoyalBase<King>.Generator.Generate(3));
	}
	public class Prince : RoyalBase<Prince> { }
	public class Duke : RoyalBase<Duke> { }
	public class Earl : RoyalBase<Earl> { }
	public class Baron : RoyalBase<Baron> { }

	public class RoyalSeeder
	{
		private readonly IElasticClient _client;
		private readonly IndexName _index;

		public RoyalSeeder(IElasticClient client, IndexName index) { this._client = client; this._index = index; }

		public void Seed()
		{
			var create = this._client.CreateIndex(this._index, c => c		
				.Settings(s=>s
					.NumberOfReplicas(0)
					.NumberOfShards(1)
				)
				.Mappings(map=>map
					.Map<King>(m=>m.AutoMap()
						.Properties(props=>
							RoyalProps(props)
							.Nested<King>(n=>n.Name(p=>p.Foes).AutoMap())
						)
					)
					.Map<Prince>(m=>m.AutoMap().Properties(RoyalProps).Parent<King>())
					.Map<Duke>(m=>m.AutoMap().Properties(RoyalProps).Parent<Prince>())
					.Map<Earl>(m=>m.AutoMap().Properties(RoyalProps).Parent<Duke>())
					.Map<Baron>(m=>m.AutoMap().Properties(RoyalProps).Parent<Earl>())
				 )
			);

			var bulk = new BulkDescriptor();
			IndexAll(bulk, () =>  King.Generator.Generate(2), indexChildren: king =>
				IndexAll(bulk, () => Prince.Generator.Generate(2), king.Name, prince =>
					IndexAll(bulk, () => Duke.Generator.Generate(3), prince.Name, duke =>
						IndexAll(bulk, () => Earl.Generator.Generate(5), duke.Name, earl =>
							IndexAll(bulk, () => Baron.Generator.Generate(1), earl.Name)
						)
					)
				)
			);
			this._client.Refresh(this._index);
		}

		private PropertiesDescriptor<TRoyal> RoyalProps<TRoyal>(PropertiesDescriptor<TRoyal> props) where TRoyal : class, IRoyal => 
			props.String(s => s.Name(p => p.Name).NotAnalyzed());

		private void IndexAll<TRoyal>(BulkDescriptor bulk, Func<IEnumerable<TRoyal>> create, string parent = null, Action<TRoyal> indexChildren = null)
			where TRoyal : class, IRoyal
		{
			var current = create();
			//looping twice horrible but easy to debug :)
			var royals = current as IList<TRoyal> ?? current.ToList();
			foreach (var royal in royals)
			{
				var royal1 = royal;
				bulk.Index<TRoyal>(i => i.Document(royal1).Index(this._index).Parent(parent));
			}
			if (indexChildren == null) return;
			foreach (var royal in royals)
				indexChildren(royal);
		}

	}


	[CollectionDefinition(IntegrationContext.OwnIndex)]
	public class InnerHitsCluster : ClusterBase, ICollectionFixture<InnerHitsCluster>, IClassFixture<EndpointUsage> { }

	[Collection(IntegrationContext.OwnIndex)]
	public abstract class InnerHitsApiTestsBase<TRoyal> : ApiIntegrationTestBase<ISearchResponse<TRoyal>, ISearchRequest, SearchDescriptor<TRoyal>, SearchRequest<TRoyal>>
		where TRoyal : class, IRoyal
	{
		public InnerHitsApiTestsBase(InnerHitsCluster cluster, EndpointUsage usage) : base(cluster, usage) { }

		protected override void BeforeAllCalls(IElasticClient client, IDictionary<ClientCall, string> values) => new RoyalSeeder(this.Client, this.Index).Seed();

		protected override LazyResponses ClientUsage() => Calls(
			fluent: (client, f) => client.Search<TRoyal>(f),
			fluentAsync: (client, f) => client.SearchAsync<TRoyal>(f),
			request: (client, r) => client.Search<TRoyal>(r),
			requestAsync: (client, r) => client.SearchAsync<TRoyal>(r)
		);

		protected IndexName Index => Index(CallIsolatedValue);

		protected override bool ExpectIsValid => true;
		protected override int ExpectStatusCode => 200;
		protected override HttpMethod HttpMethod => HttpMethod.DELETE;
		protected override string UrlPath => $"/{CallIsolatedValue},x/_query?ignore_unavailable=true";

		protected override bool SupportsDeserialization => false;

		protected override object ExpectJson { get; } = new
		{
			query = new
			{
				ids = new
				{
					types = new[] { "project" },
					values = new[] { Project.Projects.First().Name, "x" }
				}
			}
		};
		protected override SearchDescriptor<TRoyal> NewDescriptor() => new SearchDescriptor<TRoyal>().Index(this.Index);
	}

	[Collection(IntegrationContext.OwnIndex)]
	public abstract class GlobalInnerHitsApiTests : InnerHitsApiTestsBase<Duke>
	{
		public GlobalInnerHitsApiTests(InnerHitsCluster cluster, EndpointUsage usage) : base(cluster, usage) { }

		protected override Func<SearchDescriptor<Duke>, ISearchRequest> Fluent => s => s
			.Index(this.Index)
			.InnerHits(ih => ih
				.Type<Earl>("earls", g => g
					.Size(5)
					.InnerHits(iih => iih
						.Type<Baron>("barons")
					)
					.FielddataFields(p => p.Name)
				)
			);
	}
}