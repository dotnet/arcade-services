export interface Asset {
  name: string;
  version: string;
  locations?: {type: string; location: string; }[];
}
