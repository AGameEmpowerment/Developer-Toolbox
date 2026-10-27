from toolbox_samples.config import load_settings
from toolbox_samples.handlers import lambda_handler
from toolbox_samples.resources import ResourceSamples

samples = ResourceSamples(load_settings())


def handler(event, context):
    return lambda_handler(event, samples)
