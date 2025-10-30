import os
import contextlib
from fastapi import FastAPI, HTTPException
import uvicorn
from pydantic import BaseModel
from agent_framework.azure import AzureOpenAIResponsesClient
from agent_framework import ChatAgent
from azure.identity import DefaultAzureCredential
import opentelemetry.instrumentation.fastapi as otel_fastapi
import telemetry


@contextlib.asynccontextmanager
async def lifespan(app):
    """Configure OpenTelemetry on startup."""
    # Configure base OpenTelemetry infrastructure
    telemetry.configure_opentelemetry()

    yield


# Define Pydantic models
class Attraction(BaseModel):
    name: str
    description: str
    address: str
    rating: float
    operating_hours: str

class CityAttractions(BaseModel):
    city: str
    attractions: list[Attraction]

# Initialize FastAPI app
app = FastAPI(lifespan=lifespan)
otel_fastapi.FastAPIInstrumentor.instrument_app(app, exclude_spans=["send"])

# Root endpoint
@app.get("/")
async def root():
    return {"message": "FastAPI is running"}

# Initialize the Agent
azure_credential = DefaultAzureCredential()

# Get configuration from environment
endpoint = os.environ.get("AZURE_OPENAI_ENDPOINT")
deployment_name = os.environ.get("MODEL_NAME", "gpt-4o-mini")

# Create Azure OpenAI Responses client
chat_client = AzureOpenAIResponsesClient(
    endpoint=endpoint,
    deployment_name=deployment_name,
    credential=azure_credential
)

# Create the agent
agent = ChatAgent(
    chat_client=chat_client,
    instructions="You are an expert local guide. Provide detailed information about attractions in the specified city."
)

# Endpoint to get attractions for a city
@app.post("/attractions")
async def get_attractions(query: str):
    try:
        # Use the agent to get structured attractions data
        structured_result = await agent.run(f"{query}", response_format=CityAttractions)

        # Get the structured output
        city_attractions: CityAttractions = structured_result.value  # type: ignore

        # Use the agent to provide a more user-friendly response
        result = await agent.run(f"""Please provide a detailed list of attractions in {city_attractions.city} with the
                                 following details:
                                 {city_attractions.attractions}""")

        return result.text
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
    
# start app
if __name__ == "__main__":
    port = int(os.environ.get('PORT', 8000))
    uvicorn.run(app, host="0.0.0.0", port=port)