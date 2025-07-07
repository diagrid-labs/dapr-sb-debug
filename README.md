# Dapr Pub/Sub Reliability Tester

This project is a .NET Dapr application designed to test message delivery and resilience using Azure Service Bus. It acts as both a publisher (sending orders) and a subscriber (receiving orders), with features for controlled publishing concurrency and configurable deterministic message failures.

Messages not successfully processed by the application will be retried by Dapr and Azure Service Bus, eventually moving to a Dead Letter Queue (DLQ) if retries are exhausted.

This quickstart is adapted from the official Dapr [pub/sub quickstart for C# SDK](https://github.com/dapr/quickstarts/tree/master/pub_sub/csharp/sdk)

---

## 1. Prerequisites

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
        value: "<YOUR_SERVICE_BUS_CONNECTION_STRING>" # REPLACE THIS
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


## 3. Run in Kubernetes

1.  **Apply K8s Manifests:**
    Apply the Dapr component, subscription, and application deployment to your cluster.
    ```bash
    kubectl apply -f components/
    kubectl apply -f deployment.yaml
    ```
    *This applies `pubsub.yaml` and `subscribe.yaml` (from `components/`) and `deployment.yaml`.*
2.  **Check Logs in K8s:**
    When running in Kubernetes, you'll see output from both your application container and the Dapr sidecar.
    * **Application logs**: Show what your .NET app is doing (publishing, receiving, failing).
    * **Dapr sidecar logs (`daprd`)**: Provide detailed information on Dapr's internal operations, such as message routing, retries, and interactions with Service Bus. Use these to diagnose connectivity issues or Dapr's handling of failed messages.

    ```bash
    kubectl logs deployments/sb-debug -f  # Application logs (app ID from deployment.yaml)
    kubectl logs deployments/sb-debug -c daprd -f  # Dapr sidecar logs
    ```

3.  **Check Service Bus Dead-Letter Queue (DLQ):**
    After running with `SUBSCRIBER_FAIL_RATE > 0`, inspect the DLQ via the Azure portal for your Service Bus queue.

    ```bash
      az servicebus queue show \
          --resource-group <YOUR_RG_GROUP>\
          --namespace-name <NS_MAME>> \
          --name dead-letter-orders \
          --query countDetails.activeMessageCount  
    ```



---

## 4. Clean Up

1.  **Delete K8s Resources:**
    ```bash
    kubectl delete -f deployment.yaml
    kubectl delete -f components/subscribe.yaml
    kubectl delete -f components/pubsub.yaml
    ```

## Expected Results
*Successful Run (No Failures)*. `SUBSCRIBER_FAIL_RATE=0.0`

```
--- Subscriber received count: 100/100 ---

--- MESSAGE DELIVERY REPORT ---
Total messages attempted to publish: 100
Total messages successfully received: 100

All messages accounted for. No message loss detected.
-------------------------------
```

*Run with Failures Enabled* `SUBSCRIBER_FAIL_RATE=0.5`

```
--- MESSAGE DELIVERY REPORT ---
Total messages attempted to publish: 10
Total messages successfully received: 5

!!! MESSAGE LOSS DETECTED !!!
Number of lost messages: 5
Lost Order IDs: [1, 2, 3, 4, 5]
-------------------------------
```

## Option 2: Split Mode (Producer + Consumer)
Ensure your subscription is `scoped` to the consumer app: 

```yaml
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: orders-subscription
spec:
  topic: orders
  routes:
    default: /orders
  pubsubname: orderpubsub
scopes:
- sb-debug-consumer
```

Apply Components
```
# Before deploying consumer, apply components first
kubectl apply -f pubsub.yaml
kubectl apply -f subscription.yaml
```

Deploy Consumer and wait for ready 

```
kubectl apply -f ./k8s/consumer/deployment.yaml
kubectl wait --for=condition=ready pod -l app=sb-debug-consumer --timeout=60s
```

Deploy Producer 

```
kubectl apply -f ./k8s/producer/deployment.yaml
```

Monitor logs after deployment
```
kubectl logs deployments/sb-debug-consumer -f  # Consumer logs
kubectl logs deployments/sb-debug-producer -f 
```