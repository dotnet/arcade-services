import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { Subscription } from 'src/maestro-client/models';

@Component({
  selector: 'mc-subscriptions-table',
  templateUrl: './subscriptions-table.component.html',
  styleUrls: ['./subscriptions-table.component.scss']
})
export class SubscriptionsTableComponent implements OnChanges {

  @Input() public rootId?: number;
  @Input() public subscriptionsList?: Record<string, Subscription[]>;
  @Input() public includeSubToolsets?: boolean;
  public currentTab?: string;

  get branches() {
    return Object.keys(this.subscriptionsList || {});
  }

  constructor() { }

  ngOnChanges(changes: SimpleChanges) {
    this.currentTab = this.branches[0];
  }
}
