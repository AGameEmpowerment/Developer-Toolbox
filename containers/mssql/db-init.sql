/*
    Deployment script placeholder for the database.
    Use the Visual Studio SQL Project to generate and publish the full deployment script.
*/

GO
SET ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS, ARITHABORT, CONCAT_NULL_YIELDS_NULL, QUOTED_IDENTIFIER ON;

SET NUMERIC_ROUNDABORT OFF;


CREATE DATABASE [AiCoaches]
GO

USE [AiCoaches];
GO

CREATE TABLE [dbo].[ProductsServices]
(
    [Id]                UNIQUEIDENTIFIER    NOT NULL CONSTRAINT [DF_Dbo_ProductsServices_Id] DEFAULT (NewSequentialId()),
    [Name]              NVARCHAR (255)      NOT NULL,
    [Description]       NVARCHAR (MAX)      NULL,
    [Price]             DECIMAL (10, 2)     NOT NULL,
    [IsProduct]         BIT                 NOT NULL CONSTRAINT [DF_Dbo_IsProduct_Id] DEFAULT (0),

    CONSTRAINT [PK_ProductsServices] PRIMARY KEY CLUSTERED ([Id] ASC),
);
GO