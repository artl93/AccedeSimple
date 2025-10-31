"""
A2A Protocol Hosting for Agent Framework Agents

This module provides A2A (Agent-to-Agent) protocol hosting capabilities for
Python agents built with the agent-framework. It exposes agents via HTTP endpoints
that follow the A2A protocol specification

This is a temporary implementation that will be replaced when the official
agent-framework-a2a-hosting package becomes available.

A2A Protocol: https://a2a-protocol.org/latest/
"""

from typing import Any, Optional
from pydantic import BaseModel, Field
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
import uuid
from datetime import datetime


# A2A Protocol Models
class AgentCapabilities(BaseModel):
    """Agent capabilities model"""
    streaming: bool = False
    pushNotifications: bool = False


class AgentSkill(BaseModel):
    """Agent skill definition"""
    id: str
    name: str
    description: str
    tags: list[str] = Field(default_factory=list)
    examples: list[str] = Field(default_factory=list)


class AgentCard(BaseModel):
    """
    Agent Card - describes the agent's capabilities and metadata.
    Used for agent discovery via /.well-known/agent-card.json
    """
    name: str
    description: str
    url: str = ""
    protocolVersion: str = "2024-11-05"
    defaultInputModes: list[str] = Field(default_factory=lambda: ["text"])
    defaultOutputModes: list[str] = Field(default_factory=lambda: ["text"])
    version: str = "1.0.0"
    capabilities: AgentCapabilities = Field(default_factory=AgentCapabilities)
    skills: list[AgentSkill] = Field(default_factory=list)
    preferredTransport: str = "http"

    class Config:
        json_schema_extra = {
            "example": {
                "name": "LocalGuide",
                "description": "Provides information about local attractions and destinations",
                "url": "http://localhost:8000",
                "protocolVersion": "2024-11-05",
                "defaultInputModes": ["text"],
                "defaultOutputModes": ["text"],
                "version": "1.0.0",
                "capabilities": {
                    "streaming": False,
                    "pushNotifications": False
                },
                "skills": [],
                "preferredTransport": "http"
            }
        }


class A2AMessage(BaseModel):
    """A2A message format for agent communication"""
    role: str  # "user", "assistant", "system"
    content: str
    name: Optional[str] = None
    metadata: dict[str, Any] = Field(default_factory=dict)


class JsonRpcRequest(BaseModel):
    """JSON-RPC 2.0 request"""
    jsonrpc: str = "2.0"
    method: str
    params: dict[str, Any] = Field(default_factory=dict)
    id: Optional[str | int] = None


class JsonRpcResponse(BaseModel):
    """JSON-RPC 2.0 response"""
    model_config = {"extra": "forbid"}  # Don't include null fields

    jsonrpc: str = "2.0"
    result: Optional[dict[str, Any]] = None
    error: Optional[dict[str, Any]] = None
    id: Optional[str | int] = None

    def model_dump(self, **kwargs):
        # Override to exclude None values
        data = super().model_dump(**kwargs)
        return {k: v for k, v in data.items() if v is not None or k in ['jsonrpc', 'id']}


class MessageSendParams(BaseModel):
    """Parameters for message/send method"""
    message: dict[str, Any]  # Single message, not array
    contextId: Optional[str] = None


class MessagePart(BaseModel):
    """A2A Message Part"""
    kind: str = "text"  # Can be "text", "file", "data"
    text: Optional[str] = None


class AgentMessage(BaseModel):
    """A2A Agent Message"""
    kind: str = "message"
    role: str
    messageId: str = Field(default_factory=lambda: str(uuid.uuid4()))
    parts: list[MessagePart] = Field(default_factory=list)


class TaskStatus(BaseModel):
    """A2A Task Status object"""
    state: str  # "completed", "failed", "in-progress", "canceled", "rejected"
    message: Optional[AgentMessage] = None


class Artifact(BaseModel):
    """A2A Artifact object"""
    kind: str = "artifact"
    artifactId: str = Field(default_factory=lambda: str(uuid.uuid4()))
    parts: list[MessagePart] = Field(default_factory=list)
    metadata: dict[str, Any] = Field(default_factory=dict)


class Task(BaseModel):
    """A2A Task object"""
    kind: str = "task"  # Discriminator for polymorphic deserialization
    id: str
    contextId: Optional[str] = None  # Required in response (can be null)
    status: TaskStatus
    history: list[dict[str, Any]] = Field(default_factory=list)
    artifacts: list[Artifact] = Field(default_factory=list)
    metadata: dict[str, Any] = Field(default_factory=dict)


def create_agent_card(name: str, description: str, base_url: str) -> AgentCard:
    """
    Create an AgentCard for the agent.

    Args:
        name: The agent's name
        description: Description of the agent's purpose and capabilities
        base_url: The base URL where this agent is hosted

    Returns:
        AgentCard describing the agent
    """
    # Create default skill based on agent description
    default_skill = AgentSkill(
        id=f"id_{name.lower().replace(' ', '_')}",
        name=name,
        description=description,
        tags=["python-agent", "agent-framework"],
        examples=[f"Ask {name} a question"]
    )

    capabilities = AgentCapabilities(
        streaming=False,
        pushNotifications=False
    )

    return AgentCard(
        name=name,
        description=description,
        url=base_url,
        protocolVersion="2024-11-05",
        defaultInputModes=["text"],
        defaultOutputModes=["text"],
        version="1.0.0",
        capabilities=capabilities,
        skills=[default_skill],
        preferredTransport="http"
    )


def extract_user_message(message: dict[str, Any]) -> str:
    """
    Extract the user's message from A2A message object.

    Args:
        message: Single message dict from A2A request with parts

    Returns:
        The user's message text
    """
    # The message has a 'parts' array with text parts
    parts = message.get("parts", [])

    # Concatenate all text parts
    text_parts = []
    for part in parts:
        if part.get("kind") == "text":
            text = part.get("text", "")
            if text:
                text_parts.append(text)

    if text_parts:
        return " ".join(text_parts)

    raise ValueError("No text content found in message parts")


async def handle_message_send(agent, params: dict[str, Any]) -> Task:
    """
    Handle the message/send JSON-RPC method.

    Args:
        agent: The agent instance
        params: Method parameters containing message

    Returns:
        Task object with the agent's response
    """
    task_id = str(uuid.uuid4())
    context_id = params.get("contextId")
    message = params.get("message", {})

    try:
        # Extract user message from parts
        user_message = extract_user_message(message)

        # Call the agent
        result = await agent.run(user_message)

        # Create response message in A2A format for history
        response_message = {
            "kind": "message",
            "role": "agent",
            "messageId": str(uuid.uuid4()),
            "parts": [
                {
                    "kind": "text",
                    "text": result.text
                }
            ]
        }

        # Create artifact with agent's response (this is what C# extracts)
        response_artifact = Artifact(
            kind="artifact",
            parts=[MessagePart(kind="text", text=result.text)],
            metadata={}
        )

        # Create completed task
        return Task(
            id=task_id,
            contextId=context_id,
            status=TaskStatus(
                state="completed",
                message=AgentMessage(
                    role="agent",
                    parts=[MessagePart(text="Task completed successfully")]
                )
            ),
            history=[message, response_message],
            artifacts=[response_artifact],
            metadata={}
        )

    except Exception as e:
        # Return failed task
        return Task(
            id=task_id,
            contextId=context_id,
            status=TaskStatus(
                state="failed",
                message=AgentMessage(
                    role="agent",
                    parts=[MessagePart(text=f"Error: {str(e)}")]
                )
            ),
            history=[message] if message else [],
            artifacts=[],
            metadata={"error": str(e)}
        )


def register_a2a_endpoints(app: FastAPI, agent, agent_name: str, agent_description: str, base_url: str = "http://localhost:8000"):
    """
    Register A2A protocol endpoints on a FastAPI application.

    This function adds the following endpoints:
    - GET /.well-known/agent-card.json - Returns the agent card
    - POST / - Handles A2A task requests

    Args:
        app: FastAPI application instance
        agent: The agent-framework agent instance (must have .run() method)
        agent_name: Name of the agent for the agent card
        agent_description: Description of the agent for the agent card
        base_url: The base URL where this agent is hosted (default: "http://localhost:8000")

    Example:
        ```python
        agent = ChatAgent(...)
        register_a2a_endpoints(app, agent, "LocalGuide", "Provides travel information", "http://myservice:8000")
        ```
    """

    # Create the agent card
    agent_card = create_agent_card(agent_name, agent_description, base_url)

    @app.get("/.well-known/agent-card.json")
    async def get_agent_card():
        """
        A2A Agent Discovery Endpoint

        Returns the agent card describing this agent's capabilities.
        This follows the A2A protocol's agent discovery mechanism.
        """
        return JSONResponse(content=agent_card.model_dump())

    @app.post("/")
    async def handle_a2a_jsonrpc(request: Request):
        """
        A2A JSON-RPC Endpoint

        Handles JSON-RPC 2.0 requests following the A2A protocol specification.
        Supports methods like message/send, message/stream, tasks/get, etc.
        """
        try:
            # Parse JSON-RPC request
            body = await request.json()
            rpc_request = JsonRpcRequest(**body)

            # Route to appropriate handler based on method
            if rpc_request.method == "message/send":
                task = await handle_message_send(agent, rpc_request.params)
                response = JsonRpcResponse(
                    jsonrpc="2.0",
                    result=task.model_dump(),
                    id=rpc_request.id
                )
                return JSONResponse(
                    content=response.model_dump(),
                    media_type="application/json"
                )

            elif rpc_request.method == "tasks/get":
                # For now, we don't store tasks, so return an error
                response = JsonRpcResponse(
                    jsonrpc="2.0",
                    error={
                        "code": -32601,
                        "message": "Method not implemented: tasks/get"
                    },
                    id=rpc_request.id
                )
                return JSONResponse(content=response.model_dump(), status_code=501)

            else:
                # Method not found
                response = JsonRpcResponse(
                    jsonrpc="2.0",
                    error={
                        "code": -32601,
                        "message": f"Method not found: {rpc_request.method}"
                    },
                    id=rpc_request.id
                )
                return JSONResponse(content=response.model_dump(), status_code=404)

        except ValueError as e:
            # Invalid params
            response = JsonRpcResponse(
                jsonrpc="2.0",
                error={
                    "code": -32602,
                    "message": f"Invalid params: {str(e)}"
                },
                id=body.get("id") if isinstance(body, dict) else None
            )
            return JSONResponse(content=response.model_dump(), status_code=400)

        except Exception as e:
            # Internal error
            response = JsonRpcResponse(
                jsonrpc="2.0",
                error={
                    "code": -32603,
                    "message": f"Internal error: {str(e)}"
                },
                id=body.get("id") if isinstance(body, dict) else None
            )
            return JSONResponse(content=response.model_dump(), status_code=500)
