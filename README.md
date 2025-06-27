# Dapr Pub/Sub Reliability Tester

This project is a .NET Dapr application testing message delivery and resilience with Azure Service Bus. It acts as both publisher and subscriber, featuring controlled publishing concurrency and configurable deterministic message failures.

Messages not successfully processed will be retried by Dapr/Azure Service Bus and may move to a Dead Letter Queue (DLQ).

## 1. Prerequisites

* **.NET 8 SDK**: For local development.
* **Dapr CLI**: For local Dapr integration.
* **Kubernetes Cluster**: With Dapr installed, for K8s deployment.
* **Azure Service Bus**: An existing Service Bus Namespace and a queue 
    * **Get your Service Bus Connection String**:
    * **Update `components/pubsub.yaml`**: Replace `value: "secret value"` with your Service Bus connection string.

    ```yaml
    # components/pubsub.yaml (excerpt - update this part)
    metadata:
      name: orderpubsub
    spec:
      type: pubsub.azure.servicebus.queues
      version: v1
      metadata:
      - name: connectionString
        value: "<YOUR_SERVICE_BUS_CONNECTION_STRING>" 
    ```
    Ensure `components/subscribe.yaml` aligns with your Service Bus configuration
    ```yaml
    # components/subscribe.yaml (excerpt)
    apiVersion: dapr.io/v2alpha1
    kind: Subscription
    metadata:
      name: orders-subscription
    spec:
      topic: orders
      routes:
        default: /orders
      pubsubname: orderpubsub
      deadLetterTopic: dead-letter-orders # Messages that exhaust retries will go here.
    ```

## 2. Run Locally with Dapr

1.  **Go to the project folder:**
    ```bash
    cd order-processor
    ```

2.  **Build & Restore (first time or after code changes):**
    ```bash
    dotnet restore
    dotnet build
    ```

3.  **Run Scenarios with Dapr:**

    ### Scenario A: Normal Run (No Subscriber Failures)
    ```bash
    MESSAGE_COUNT=100 SUBSCRIBER_FAIL_RATE=0.0 MAX_CONCURRENT_PUBLISHES=5 \
    dapr run --app-id order-processor --log-level debug --resources-path ../components --app-port 7006 -- dotnet run
    ```

    ### Scenario B: Debugging DLQ Failures
    ```bash
    MESSAGE_COUNT=100 SUBSCRIBER_FAIL_RATE=0.1 MAX_CONCURRENT_PUBLISHES=5 \
    dapr run --app-id order-processor --log-level debug --resources-path ../components --app-port 7006 -- dotnet run
    ```
    * `MESSAGE_COUNT`: Total messages.
    * `SUBSCRIBER_FAIL_RATE`: % of messages to intentionally fail (e.g., `0.1` for 10%).
    * `MAX_CONCURRENT_PUBLISHES`: Limits concurrent message sends.

4.  **Stop:** Press `Ctrl+C`.

## 3. Run in Kubernetes

1.  **Apply K8s Manifests:**
    ```bash
    kubectl apply -f components/
    kubectl apply -f deployment.yaml
    ```
    *This applies `pubsub.yaml` and `subscribe.yaml` (from `components/`) and `deployment.yaml`.*

2.  **Check Logs in K8s:**
    ```bash
    kubectl logs deployments/sb-debug -f  # Application logs
    kubectl logs deployments/sb-debug -c daprd -f  # Dapr sidecar logs
    ```

3.  **Check Service Bus Dead-Letter Queue (DLQ):**
    After running with `SUBSCRIBER_FAIL_RATE > 0`, inspect the DLQ via the Azure portal for your Service Bus queue (`orders`).

## 4. Clean Up

1.  **Delete K8s Resources:**
    ```bash
    kubectl delete -f deployment.yaml
    kubectl delete -f components/subscribe.yaml
    kubectl delete -f components/pubsub.yaml
    kubectl delete secret servicebus-secret
    ```