# Pull Docker images used in the setup

if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Host "Docker images and container setup started."

    ## Pull the Docker images
    docker pull datalust/seq
    docker pull mcr.microsoft.com/azure-messaging/servicebus-emulator
    docker pull mcr.microsoft.com/azure-sql-edge
    docker pull mcr.microsoft.com/azure-storage/azurite
    docker pull mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator
    docker pull mcr.microsoft.com/dotnet/sdk
    docker pull mcr.microsoft.com/dotnet/aspnet
    docker pull mcr.microsoft.com/mssql/server
    docker pull redis
    docker pull redis/redisinsight
    docker pull rnwood/smtp4dev
    docker pull wiremock/wiremock

}

Write-Host "Docker images and container setup completed."
