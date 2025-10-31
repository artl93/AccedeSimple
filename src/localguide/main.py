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
import a2a_hosting



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

# Get the port from environment (same as uvicorn will use)
port = int(os.environ.get('PORT', 8000))

# Register A2A protocol endpoints
# This will handle "/" for A2A tasks and "/.well-known/agent-card.json" for discovery
# Use the service name "localguide" as it will be resolved by Aspire service discovery
a2a_hosting.register_a2a_endpoints(
    app,
    agent,
    agent_name="LocalGuide",
    agent_description="Provides detailed information about local attractions and travel destinations",
    base_url=f"http://localhost:{port}"
)
    
# start app
if __name__ == "__main__":
    port = int(os.environ.get('PORT', 8000))
    uvicorn.run(app, host="0.0.0.0", port=port)