// Keep this probe independent of document APIs and any fingerprinting-site code.
postMessage({
    selfIsGlobal: self === globalThis,
    document: typeof document,
    navigator: typeof navigator,
    WorkerNavigator: typeof WorkerNavigator,
    WorkerGlobalScope: typeof WorkerGlobalScope,
    DedicatedWorkerGlobalScope: typeof DedicatedWorkerGlobalScope,
    isWorkerGlobal: typeof WorkerGlobalScope === 'function' ? self instanceof WorkerGlobalScope : null,
    crypto: typeof crypto,
    performance: typeof performance,
    platform: typeof navigator === 'undefined' ? null : navigator.platform,
    userAgent: typeof navigator === 'undefined' ? null : navigator.userAgent
});
