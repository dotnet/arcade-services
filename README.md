# Arcade Services

## Overview

This repo is home of the services that help us construct .NET. Mainly, you can find the **Product Construction Service** (previously *Maestro*) dependency flow system and the [Darc CLI tool](./docs/Darc.md).

The service's main responsibility is opening and managing dependency update pull requests in .NET repositories. It is also responsible for [the code flow subscription between product repositories and the VMR](https://github.com/dotnet/dotnet/tree/main/docs/VMR-Full-Code-Flow.md).

## Development

See instructions on how to run the code [here](docs/DevGuide.md).

## Contribution

We welcome contributions! Please follow the [Code of Conduct](CODE-OF-CONDUCT.md).

## Filing issues

This repo should contain issues that are tied to Maestro and Darc.

### License

.NET (including this repo) is licensed under the [MIT license](LICENSE.TXT).
