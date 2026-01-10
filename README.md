# couchbase-aspire

Couchbase hosting and client integrations for Microsoft Aspire.

## Getting Started

See the [Hosting](src/Couchbase.Aspire.Hosting/README.md) and [Client](src/Couchbase.Aspire.Client/README.md) documentation for details on how to get started.

## Contributing

To run locally, you can use the Aspire Test AppHost project located in the `tests/Aspire.Test.AppHost` directory. If you have installed the Aspire CLI tool, you can run the AppHost with the following command:

```sh
aspire run
```

## Releasing

Create a release in GitHub with a tag name in the format `release/vX.Y.Z` (e.g. `release/v1.0.0`).
This will trigger the GitHub Actions workflow to build and publish the NuGet package to nuget.org.
