# FullTime API

FullTime API is an open source RESTful API that retrieves football data from [FullTime](https://fulltime.thefa.com/). It scrapes the FullTime website provided by The FA to provide structured, JSON-formatted data for fixtures, match results, league tables, and player statistics.

Example of a BETA verison of this API is in use at: [Axbridge United](https://utb2.netlify.app/league)

## Features

- **Fixtures**: Retrieve upcoming fixtures for various leagues.
- **Results**: Access match results from completed fixtures.
- **League Tables**: Get up-to-date league standings.
- **Player Stats**: View detailed statistics for players, including games played, goals scored, and cards received.

## Credits

- A big thank you to [jadgray/FullTimeApi](https://github.com/jadgray/FullTimeApi/tree/main) for the initial starting point and inspiration for this project.

## Getting Started

### Prerequisites

- .NET 8 or later (download from https://dotnet.microsoft.com/download/dotnet/8.0)
- An internet connection (to fetch data from [FullTime](https://fulltime.thefa.com/))
- IDE

### Installation

1. Clone the Repository:

   git clone https://github.com/jwhughes-work/FullTimeAPI.git
   cd FullTimeAPI

2. Build the Project:

   dotnet build

3. Run the Project:

   dotnet run

   The API will start and listen on the configured port. You can test the endpoints using your browser, Postman, or any API testing tool.

## API Endpoints

### Fixtures

- GET /api/fixtures/{leagueId}?teamName={OptionalTeamName}  
  Retrieves fixtures for the specified league. You can optionally filter the fixtures by providing a specific team name.

## Contributing

Contributions are welcome! If you’d like to improve the API or add new features.
Feel free to open issues if you have suggestions or find bugs.

## License

This project is open source and available under the MIT License.

## Acknowledgements

- **FullTime**: Data is scraped from [FullTime](https://fulltime.thefa.com/), the official website of The FA.
- **Starting Point**: Thanks to [jadgray/FullTimeApi](https://github.com/jadgray/FullTimeApi/tree/main) for providing a solid foundation for this project.

---

Enjoy using the FullTime API! If you have any questions or feedback, please open an issue on GitHub.
