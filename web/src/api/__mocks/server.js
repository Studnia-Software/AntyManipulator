import express from 'express';
import cors from 'cors';
const app = express()
const port = 3000
import { apiResponse } from './response.ts';

app.use(cors());

app.post('/api/checks', (req, res) => {
  res.json(apiResponse);
})

app.listen(port, () => {
  console.log(`Example app listening on port ${port}`)
})