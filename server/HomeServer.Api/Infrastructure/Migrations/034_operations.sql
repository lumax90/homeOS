CREATE TABLE operation_metrics(at TEXT PRIMARY KEY,metrics_json TEXT NOT NULL);
CREATE TABLE configuration_receipts(id TEXT PRIMARY KEY,request_hash TEXT NOT NULL,result_json TEXT NOT NULL,created_at TEXT NOT NULL);
