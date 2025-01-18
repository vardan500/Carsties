using AutoMapper;
using SearchService;
using MongoDB.Entities;
using MongoDB.Driver;
using System.Net;
using Polly;
using Polly.Extensions.Http;
using MassTransit;



var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient<AuctionSvcHttpClient>().AddPolicyHandler(GetPolicy());
builder.Services.AddMassTransit(x =>
{
    x.AddConsumersFromNamespaceContaining<AuctionCreatedConsumer>();
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("search", false));

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.ReceiveEndpoint("search-auction-created", e =>
        {
            e.UseMessageRetry(r => r.Interval(5, 5));

            e.ConfigureConsumer<AuctionCreatedConsumer>(context);
        });

    });

    //AuctionUpdated

});
builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseAuthorization();

app.MapControllers();


app.Lifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        await DbInitializer.InitDb(app);
    }
    catch (Exception e)
    {
        Console.WriteLine(e);
    }
});

await DB.InitAsync("SearchDb", MongoClientSettings.FromConnectionString(builder.Configuration.GetConnectionString("MongoDbConnection")));


await DB.Index<Item>()
.Key(x => x.Make, KeyType.Text)
.Key(x => x.Model, KeyType.Text)
.Key(x => x.Color, KeyType.Text)
.CreateAsync();

app.Run();

static IAsyncPolicy<HttpResponseMessage> GetPolicy()
    => HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == HttpStatusCode.NotFound)
        .WaitAndRetryForeverAsync(_ => TimeSpan.FromSeconds(3));