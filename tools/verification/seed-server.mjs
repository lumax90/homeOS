import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {join} from 'node:path';
const root=process.argv[2];if(!root)throw Error('Seed directory required.');
createServer(async(request,response)=>{const name=request.url?.slice(1);if(!['meta-data','user-data'].includes(name)){response.writeHead(404).end();return;}try{const data=await readFile(join(root,name));response.writeHead(200,{'Content-Type':'text/plain','Content-Length':data.length}).end(data);}catch{response.writeHead(404).end();}}).listen(62302,'127.0.0.1');
