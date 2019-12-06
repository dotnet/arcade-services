import { Asset, Subscription } from 'src/maestro-client/models';

export class SubscriptionConflict{
  Asset?: string;
  Subscriptions?: Array<Subscription>;
  Utilized?: boolean;
}
