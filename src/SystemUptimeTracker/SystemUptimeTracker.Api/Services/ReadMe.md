# Services

## Overview

Services are where most of your application logic is run. Loads will call repositories and then use factories to convert to UI models with some potential application logic.
Saves will use a factory to convert from UI models to data models and then call repositories to save the data.

**note:** Services don't have a monopoly on repository use. It can be appropriate to use repositories in factories under some circumstances. For example, you may have a countryId on an address data model
and the UI wants the country name to display. You can make an argument that giving the factory the model you already have is enough for the service. The factory could be responsible for resolving that country id to the data it needs.
This is largely a design decision and should be made on a case by case basis. As a general rule of thumb, factories should typically call repositories that use caching to avoid multiple calls to the same data source.

## Folder Structure

Group services conceptually as appropriate. Each folder should have an Interfaces subfolder where interfaces are declared. Then implementations are siblings to the Interfaces folder. Dependency Injection resides in the DependencyInjection folder
at the root of the Services folder.




