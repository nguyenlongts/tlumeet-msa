# TLUMeet - Online Meeting System (Microservices)

---

Architecture:

* API Gateway: Ocelot
* Services:

  * Auth Service
  * Meeting Service
  * Notification Service
* Database: SQL Server (each service has its own database)
* Message Broker: Kafka (for asynchronous communication between services)

### Flow Overview

Client → API Gateway → Services → Kafka → Notification Service

### Service Architecture

Each service follows Clean Architecture:

- Domain Layer  
- Application Layer  
- Infrastructure Layer  
- API Layer  

---

## Setup & Run

### 1. Start services (Docker + Kafka)

```bash
docker-compose up --build
```

---

### 2. Database Setup

Each service uses its own database:

* AuthService → AuthDb
* MeetingService → MeetingDb

Update connection string in each service (`appsettings.json`):

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=AuthDb;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

---

### 3. Run Migration

```bash
cd AuthService
dotnet ef database update

cd MeetingService
dotnet ef database update
```

---

## Tech Stack

* .NET 8
* ASP.NET Core Web API
* Ocelot API Gateway
* SQL Server
* Docker
* Kafka

